using System.Text.RegularExpressions;
using DevPath.Application.CodeExecution;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Corrige o envio do aluno: roda o código contra cada caso de teste do exercício
/// e grava uma <see cref="CodeSubmission"/> com o resultado.
/// </summary>
/// <remarks>
/// Divisão de responsabilidades (ADR-004): o container recebe SÓ o código do aluno e a preparação
/// (SQL do dataset + setup do caso). O resultado esperado fica aqui fora, e a comparação
/// (<see cref="ResultComparer"/>) também. Assim nada secreto passa perto do código do aluno.
/// </remarks>
public sealed partial class ExerciseSubmissionService(
    DevPathDbContext db,
    IDatasetRepository datasets,
    IEnumerable<ICodeRunner> runners) : IExerciseSubmissionService
{
    /// <summary>Tamanho máximo do código enviado. Barra envios gigantes antes de gastar um container.</summary>
    public const int MaxCodeLength = 20_000;

    /// <summary>Quanto do erro técnico mostramos ao aluno no feedback.</summary>
    private const int MaxTechnicalDetailChars = 1_000;

    public async Task<SubmissionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default)
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

        if (exercise.MaxAttempts is { } maxAttempts)
        {
            var attempts = await db.CodeSubmissions.CountAsync(s => s.UserId == userId && s.ExerciseId == exerciseId, cancellationToken);
            if (attempts >= maxAttempts)
            {
                throw new InvalidOperationException($"Limite de {maxAttempts} tentativas atingido para este exercício.");
            }
        }

        var cases = TestCaseSpec.ParseList(exercise.TestCases);

        // Carrega TODOS os datasets antes de executar qualquer coisa: se faltar um, é erro de conteúdo
        // (DatasetNotFoundException), não gastamos container e não gravamos tentativa contra o aluno.
        var datasetSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in cases.Select(c => c.Dataset).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dataset = await datasets.FindBySlugAsync(slug, cancellationToken) ?? throw new DatasetNotFoundException(slug);
            datasetSql[slug] = dataset.Sql;
        }

        var passed = 0;
        var totalMs = 0;
        string? firstFailure = null;

        foreach (var testCase in cases)
        {
            var preparation = BuildPreparation(testCase.Dataset is null ? null : datasetSql[testCase.Dataset], testCase.Setup);
            var result = await runner.RunAsync(code, preparation, cancellationToken);
            totalMs += (int)result.Duration.TotalMilliseconds;

            var (failure, stop) = Evaluate(testCase, result);
            if (failure is null)
            {
                passed++;
                continue;
            }

            firstFailure ??= failure;
            if (stop)
            {
                break; // não vale a pena gastar mais containers com um código que trava
            }
        }

        var allPassed = passed == cases.Count;
        var feedback = allPassed
            ? (cases.Count == 1 ? "O caso de teste passou." : $"Todos os {cases.Count} casos de teste passaram.")
            : firstFailure!;

        var submission = CodeSubmission.Create(userId, exerciseId, code,
            allPassed ? SubmissionResult.Pass : SubmissionResult.Fail, totalMs, feedback);
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        return new SubmissionOutcome(submission.Id, submission.Result, feedback, totalMs, passed, cases.Count);
    }

    /// <summary>Dataset primeiro, depois o setup do caso (que pode, por exemplo, mudar uma linha do dataset).</summary>
    public static string? BuildPreparation(string? datasetSql, string? setup)
    {
        var parts = new[] { datasetSql, setup }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join("\n;\n", parts) + "\n";
    }

    /// <summary>Failure nulo = o caso passou. Stop = não adianta rodar os próximos casos.</summary>
    private static (string? Failure, bool Stop) Evaluate(TestCaseSpec testCase, SandboxResult result)
    {
        var prefix = $"Caso \"{testCase.Name}\": ";
        if (result.TimedOut)
        {
            return (prefix + SqlErrorTranslator.TimeoutMessage, true);
        }
        if (result.OomKilled)
        {
            return (prefix + "a execução usou memória demais.", true);
        }
        if (result.OutputTruncated)
        {
            return (prefix + "a saída ficou grande demais.", false);
        }
        if (result.ExitCode == PostgresSqlCodeRunner.SetupFailedExitCode)
        {
            return (prefix + "erro na preparação do exercício (não é culpa sua). Avise a equipe.", true);
        }
        if (result.ExitCode != 0)
        {
            var error = SqlErrorTranslator.Translate(result.Stderr);
            var detail = PsqlLocation().Replace(error.TechnicalDetail, string.Empty);
            if (detail.Length > MaxTechnicalDetailChars)
            {
                detail = detail[..MaxTechnicalDetailChars] + "…";
            }
            return ($"{prefix}{error.FriendlyMessage}\n\nDetalhe técnico:\n{detail}", error.IsTimeout);
        }

        QueryResult actual;
        try
        {
            actual = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows);
        }
        catch (FormatException)
        {
            return (prefix + "não foi possível ler o resultado da sua consulta. Tente de novo; se continuar, avise a equipe.", false);
        }

        var comparison = ResultComparer.Compare(testCase, actual);
        return (comparison.Passed ? null : prefix + comparison.Feedback, false);
    }

    // "psql:/tmp/consulta.psql:8: " só atrapalha o aluno: ele nunca viu esse arquivo.
    [GeneratedRegex(@"^psql:[^\n:]*:\d+:\s*", RegexOptions.Multiline)]
    private static partial Regex PsqlLocation();
}
