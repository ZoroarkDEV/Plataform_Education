using System.Text.RegularExpressions;
using DevPath.Application.CodeExecution;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Corrige o envio do aluno: roda o código contra cada caso de teste do exercício, monta a resposta da tela
/// (<see cref="ExecutionOutcome"/>) e grava uma <see cref="CodeSubmission"/>.
/// </summary>
/// <remarks>
/// <para>Registrado só no Worker (<c>AddSandboxExecution</c>): é o único processo que fala com o Docker (ADR-006).</para>
/// <para>
/// Divisão de responsabilidades (ADR-004): o container recebe SÓ o código do aluno e a preparação
/// (SQL do dataset + setup do caso). O resultado esperado fica aqui fora, e a comparação
/// (<see cref="ResultComparer"/>) também. A resposta mostra a tabela que o ALUNO produziu e, por caso,
/// só se passou e o que está diferente: nunca as linhas esperadas nem a solução.
/// </para>
/// </remarks>
public sealed partial class ExerciseSubmissionService(
    DevPathDbContext db,
    IDatasetRepository datasets,
    IEnumerable<ICodeRunner> runners,
    ILogger<ExerciseSubmissionService>? logger = null) : IExerciseSubmissionService
{
    private readonly ILogger _logger = logger ?? NullLogger<ExerciseSubmissionService>.Instance;

    /// <summary>Tamanho máximo do código enviado (o mesmo que a Web confere).</summary>
    public const int MaxCodeLength = ExecutionLimits.MaxCodeLength;

    /// <summary>Quanto do erro técnico vai para o "Ver detalhe técnico".</summary>
    private const int MaxTechnicalDetailChars = 1_000;

    public const string PassMessage = "Parabéns! Sua consulta passou em todos os casos de teste.";
    public const string FailMessage = "Sua consulta rodou, mas o resultado ainda não é o esperado. Veja os casos de teste.";
    public const string OomMessage = "Sua consulta usou memória demais e foi interrompida. Tente filtrar mais os dados.";
    public const string OutputTooLargeMessage = "Sua consulta devolveu dados demais. Filtre as linhas (WHERE) ou escolha menos colunas.";
    public const string SetupFailedMessage = "Houve um erro na preparação deste exercício (não é culpa sua). A tentativa não foi contada. Avise a equipe.";
    public const string UnreadableMessage = "Não foi possível ler o resultado da sua consulta. Tente de novo; se continuar, avise a equipe.";
    public const string NotRunMessage = "Não foi executado porque um caso anterior deu erro ou demorou demais.";

    private sealed record CaseRun(TestCaseSpec Case, SandboxResult Result, QueryResult? Table, string? Failure,
        ExecutionStatus? Problem, string? Technical);

    public async Task<ExecutionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length > MaxCodeLength)
        {
            throw new ArgumentException($"O código pode ter no máximo {MaxCodeLength} caracteres.", nameof(code));
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Escreva uma consulta antes de enviar.", nameof(code));
        }

        var exercise = await db.Exercises.AsNoTracking().SingleOrDefaultAsync(e => e.Id == exerciseId, cancellationToken)
            ?? throw new KeyNotFoundException($"Exercício {exerciseId} não encontrado.");

        var runner = runners.FirstOrDefault(r => string.Equals(r.Language, exercise.Language, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"Ainda não há como executar exercícios de '{exercise.Language}'.");

        var usedAttempts = 0;
        if (exercise.MaxAttempts is { } maxAttempts)
        {
            usedAttempts = await db.CodeSubmissions.CountAsync(s => s.UserId == userId && s.ExerciseId == exerciseId, cancellationToken);
            if (usedAttempts >= maxAttempts)
            {
                return ExecutionOutcome.AttemptsExhausted(maxAttempts); // nada executa, nada é gravado
            }
        }

        var cases = TestCaseSpec.ParseList(exercise.TestCases);

        // Carrega TODOS os datasets antes de executar qualquer coisa: se faltar um, é erro de conteúdo
        // (DatasetNotFoundException), não gastamos container e não gravamos tentativa contra o aluno.
        // O caso pode citar o dataset; senão, vale o dataset do exercício (campo "dataset" do conteúdo).
        string? DatasetOf(TestCaseSpec c) => c.Dataset ?? exercise.Dataset;
        var datasetSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in cases.Select(DatasetOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dataset = await datasets.FindBySlugAsync(slug, cancellationToken) ?? throw new DatasetNotFoundException(slug);
            datasetSql[slug] = dataset.Sql;
        }

        var runs = new List<CaseRun>();
        foreach (var testCase in cases)
        {
            var slug = DatasetOf(testCase);
            var preparation = BuildPreparation(slug is null ? null : datasetSql[slug], testCase.Setup);
            var run = Evaluate(testCase, await runner.RunAsync(code, preparation, cancellationToken));
            runs.Add(run);
            if (run.Problem is not null)
            {
                break; // erro, timeout ou memória: os outros casos dariam o mesmo problema. Não gasta mais containers.
            }
        }

        var totalMs = runs.Sum(r => (int)r.Result.Duration.TotalMilliseconds);
        var feedback = cases.Select((c, i) => i < runs.Count
                ? new TestCaseFeedback(c.Name, runs[i].Failure is null, runs[i].Failure)
                : new TestCaseFeedback(c.Name, false, NotRunMessage))
            .ToList();
        var passed = feedback.Count(f => f.Passed);
        var problem = runs.FirstOrDefault(r => r.Problem is not null);

        // Preparação quebrada é erro do EXERCÍCIO: não conta como tentativa do aluno.
        if (problem?.Result.ExitCode == PostgresSqlCodeRunner.SetupFailedExitCode && !problem.Result.TimedOut)
        {
            var lessonSlug = await db.Lessons.AsNoTracking().Where(l => l.Id == exercise.LessonId).Select(l => l.Slug)
                .SingleOrDefaultAsync(cancellationToken);
            _logger.LogError(
                "Erro de conteúdo: a preparação do exercício {ExerciseId} (aula {LessonSlug}, dataset {Dataset}, caso {TestCase}) " +
                "falhou no sandbox. A tentativa do aluno não foi contada. Detalhe: {Detail}",
                exercise.Id, lessonSlug, DatasetOf(problem.Case) ?? "(nenhum)", problem.Case.Name, Truncate(problem.Result.Stderr));
            return ExecutionOutcome.Failure(ExecutionStatus.Error, SetupFailedMessage, remainingAttempts:
                exercise.MaxAttempts is { } max ? max - usedAttempts : null) with { ExecutionTimeMs = totalMs };
        }

        var (status, friendly, technical) = problem is not null
            ? (problem.Problem!.Value, problem.Failure!, problem.Technical)
            : passed == cases.Count ? (ExecutionStatus.Pass, PassMessage, (string?)null) : (ExecutionStatus.Fail, FailMessage, null);

        var summary = status == ExecutionStatus.Pass
            ? (cases.Count == 1 ? "O caso de teste passou." : $"Todos os {cases.Count} casos de teste passaram.")
            : string.Join("\n", feedback.Where(f => !f.Passed).Select(f => $"Caso \"{f.Name}\": {f.Message}"));

        var submission = CodeSubmission.Create(userId, exerciseId, code,
            status == ExecutionStatus.Pass ? SubmissionResult.Pass : SubmissionResult.Fail, totalMs, summary);
        if (exercise.MaxAttempts is { } attemptLimit)
        {
            var saved = await SaveWithinLimitAsync(submission, attemptLimit, cancellationToken);
            if (saved is null)
            {
                // Perdeu a corrida: outro envio simultâneo usou a última tentativa. Este não é gravado.
                return ExecutionOutcome.AttemptsExhausted(attemptLimit);
            }
            usedAttempts = saved.Value;
        }
        else
        {
            db.CodeSubmissions.Add(submission);
            await db.SaveChangesAsync(cancellationToken);
        }

        // A tabela mostrada é a que o aluno produziu no primeiro caso (os dados "principais" do exercício).
        var table = runs[0].Table;
        var (rows, truncated) = table is null ? ([], false) : ExecutionLimits.CapRows(table);
        return new ExecutionOutcome(status, table?.Columns ?? [], rows, truncated, friendly, technical, totalMs,
            passed, cases.Count, feedback, submission.Id,
            exercise.MaxAttempts is { } limit ? limit - usedAttempts - 1 : null);
    }

    /// <summary>
    /// Grava a tentativa sem passar do limite, mesmo com dois envios do mesmo aluno ao mesmo tempo (duas abas,
    /// duas instâncias da Web, vários Workers). A contagem do início só evita gastar container à toa; a que vale
    /// é esta: um lock de transação do PostgreSQL por (aluno, exercício) serializa "contar + inserir".
    /// O lock dura só esta transação curta (não a execução no sandbox) e some sozinho no commit/rollback.
    /// </summary>
    /// <returns>Quantas tentativas já existiam antes desta; nulo se o limite já foi atingido (nada gravado).</returns>
    private async Task<int?> SaveWithinLimitAsync(CodeSubmission submission, int maxAttempts, CancellationToken cancellationToken)
    {
        var lockKey = $"devpath:tentativas:{submission.UserId:N}:{submission.ExerciseId:N}";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var used = await db.CodeSubmissions.CountAsync(
            s => s.UserId == submission.UserId && s.ExerciseId == submission.ExerciseId, cancellationToken);
        if (used >= maxAttempts)
        {
            return null; // o rollback acontece no Dispose da transação e libera o lock
        }

        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return used;
    }

    /// <summary>Dataset primeiro, depois o setup do caso (que pode, por exemplo, mudar uma linha do dataset).</summary>
    public static string? BuildPreparation(string? datasetSql, string? setup)
    {
        var parts = new[] { datasetSql, setup }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join("\n;\n", parts) + "\n";
    }

    /// <summary>
    /// Failure nulo = o caso passou. Problem não nulo = o código não rodou até o fim (erro/timeout): para nos próximos casos.
    /// </summary>
    private static CaseRun Evaluate(TestCaseSpec testCase, SandboxResult result)
    {
        if (result.TimedOut)
        {
            return new(testCase, result, null, SqlErrorTranslator.TimeoutMessage, ExecutionStatus.Timeout, null);
        }
        if (result.OomKilled)
        {
            return new(testCase, result, null, OomMessage, ExecutionStatus.Error, null);
        }
        if (result.OutputTruncated)
        {
            return new(testCase, result, null, OutputTooLargeMessage, ExecutionStatus.Error, null);
        }
        if (result.ExitCode == PostgresSqlCodeRunner.SetupFailedExitCode)
        {
            return new(testCase, result, null, SetupFailedMessage, ExecutionStatus.Error, null);
        }
        if (result.ExitCode != 0)
        {
            var error = SqlErrorTranslator.Translate(result.Stderr);
            var detail = PsqlLocation().Replace(error.TechnicalDetail, string.Empty);
            if (detail.Length > MaxTechnicalDetailChars)
            {
                detail = detail[..MaxTechnicalDetailChars] + "…";
            }
            return new(testCase, result, null, error.FriendlyMessage,
                error.IsTimeout ? ExecutionStatus.Timeout : ExecutionStatus.Error, detail);
        }

        QueryResult actual;
        try
        {
            actual = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows);
        }
        catch (FormatException)
        {
            return new(testCase, result, null, UnreadableMessage, ExecutionStatus.Error, null);
        }

        var comparison = ResultComparer.Compare(testCase, actual);
        return new(testCase, result, actual, comparison.Passed ? null : comparison.Feedback, null, null);
    }

    private static string Truncate(string text) =>
        text.Length <= MaxTechnicalDetailChars ? text : text[..MaxTechnicalDetailChars] + "…";

    // "psql:/tmp/consulta.psql:8: " só atrapalha o aluno: ele nunca viu esse arquivo.
    [GeneratedRegex(@"^psql:[^\n:]*:\d+:\s*", RegexOptions.Multiline)]
    private static partial Regex PsqlLocation();
}
