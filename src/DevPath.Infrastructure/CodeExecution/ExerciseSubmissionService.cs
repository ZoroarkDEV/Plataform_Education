using DevPath.Application.CodeExecution;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Corrige o envio do aluno: roda o código contra cada caso de teste do exercício
/// e grava uma <see cref="CodeSubmission"/> com o resultado.
/// </summary>
public sealed class ExerciseSubmissionService(DevPathDbContext db, IEnumerable<ICodeRunner> runners) : IExerciseSubmissionService
{
    /// <summary>Tamanho máximo do código enviado. Barra envios gigantes antes de gastar um container.</summary>
    public const int MaxCodeLength = 20_000;

    /// <summary>Quanto do erro (stderr) mostramos ao aluno no feedback.</summary>
    private const int MaxFeedbackErrorChars = 1_000;

    public async Task<SubmissionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length > MaxCodeLength)
        {
            throw new ArgumentException($"O código pode ter no máximo {MaxCodeLength} caracteres.", nameof(code));
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

        var cases = ExerciseTestCase.ParseList(exercise.TestCases);
        var passed = 0;
        var totalMs = 0;
        string? firstFailure = null;

        foreach (var testCase in cases)
        {
            var result = await runner.RunAsync(code, testCase.Setup, cancellationToken);
            totalMs += (int)result.Duration.TotalMilliseconds;

            var failure = DescribeFailure(testCase, result);
            if (failure is null)
            {
                passed++;
                continue;
            }

            firstFailure ??= failure;
            if (result.TimedOut || result.OomKilled)
            {
                break; // não vale a pena gastar mais containers com um código que trava
            }
        }

        var allPassed = cases.Count > 0 && passed == cases.Count;
        var feedback = allPassed
            ? $"Todos os {cases.Count} casos de teste passaram."
            : firstFailure ?? "O exercício não tem casos de teste.";

        var submission = CodeSubmission.Create(userId, exerciseId, code,
            allPassed ? SubmissionResult.Pass : SubmissionResult.Fail, totalMs, feedback);
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        return new SubmissionOutcome(submission.Id, submission.Result, feedback, totalMs, passed, cases.Count);
    }

    /// <summary>Nulo = o caso passou. Senão, uma frase em português para o aluno.</summary>
    private static string? DescribeFailure(ExerciseTestCase testCase, SandboxResult result)
    {
        if (result.TimedOut)
        {
            return $"Caso \"{testCase.Name}\": tempo esgotado. Verifique se a consulta não está pesada demais.";
        }
        if (result.OomKilled)
        {
            return $"Caso \"{testCase.Name}\": a execução usou memória demais.";
        }
        if (result.OutputTruncated)
        {
            return $"Caso \"{testCase.Name}\": a saída ficou grande demais.";
        }
        if (result.ExitCode == PostgresSqlCodeRunner.SetupFailedExitCode)
        {
            return $"Caso \"{testCase.Name}\": erro na preparação do exercício (não é culpa sua). Avise a equipe.";
        }
        if (result.ExitCode != 0)
        {
            var error = result.Stderr.Trim();
            if (error.Length > MaxFeedbackErrorChars)
            {
                error = error[..MaxFeedbackErrorChars] + "…";
            }
            return $"Caso \"{testCase.Name}\": erro ao executar.\n{error}";
        }
        if (!OutputComparer.AreEquivalent(testCase.ExpectedOutput, result.Stdout))
        {
            return $"Caso \"{testCase.Name}\": a saída não é a esperada.\nEsperado:\n{OutputComparer.Normalize(testCase.ExpectedOutput)}\nRecebido:\n{OutputComparer.Normalize(result.Stdout)}";
        }
        return null;
    }
}
