namespace DevPath.Domain.Progress;

/// <summary>Uma tentativa do aluno em um exercício, com o resultado da correção.</summary>
/// <remarks>
/// UserId aponta para o ApplicationUser (Identity, na Infrastructure). A FK existe no banco,
/// mas não há navegação aqui para o Domain não depender do Identity.
/// </remarks>
public sealed class CodeSubmission
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public string SubmittedCode { get; private set; } = string.Empty;
    public SubmissionResult Result { get; private set; }

    /// <summary>
    /// Certo, errado ou erro (#43). <c>null</c> só em tentativas gravadas antes do #43 que não passaram:
    /// naquela época erro e timeout também eram gravados só como <see cref="SubmissionResult.Fail"/>.
    /// </summary>
    public SubmissionOutcome? Outcome { get; private set; }
    public string? Feedback { get; private set; }
    public int ExecutionTimeMs { get; private set; }
    public DateTime SubmittedAt { get; private set; }

    private CodeSubmission() { }

    public static CodeSubmission Create(
        Guid userId,
        Guid exerciseId,
        string submittedCode,
        SubmissionResult result,
        int executionTimeMs,
        string? feedback = null,
        SubmissionOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(submittedCode);
        ArgumentOutOfRangeException.ThrowIfNegative(executionTimeMs);

        // Sem detalhe, o resultado diz o mínimo: Pass é certo, Fail é errado.
        var resolvedOutcome = outcome ?? (result == SubmissionResult.Pass ? SubmissionOutcome.Pass : SubmissionOutcome.Fail);
        if ((resolvedOutcome == SubmissionOutcome.Pass) != (result == SubmissionResult.Pass))
        {
            throw new ArgumentException($"Outcome {resolvedOutcome} não combina com o resultado {result}.", nameof(outcome));
        }

        return new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExerciseId = exerciseId,
            SubmittedCode = submittedCode,
            Result = result,
            Outcome = resolvedOutcome,
            ExecutionTimeMs = executionTimeMs,
            Feedback = feedback,
            SubmittedAt = DateTime.UtcNow
        };
    }
}
