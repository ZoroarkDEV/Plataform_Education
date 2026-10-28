namespace DevPath.Domain.Progress;

/// <summary>Uma tentativa do aluno em um exercício, com o resultado da correção.</summary>
/// <remarks>UserId sem navegação até a issue #9 (ApplicationUser / Identity).</remarks>
public sealed class CodeSubmission
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public string SubmittedCode { get; private set; } = string.Empty;
    public SubmissionResult Result { get; private set; }
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
        string? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(submittedCode);
        ArgumentOutOfRangeException.ThrowIfNegative(executionTimeMs);

        return new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExerciseId = exerciseId,
            SubmittedCode = submittedCode,
            Result = result,
            ExecutionTimeMs = executionTimeMs,
            Feedback = feedback,
            SubmittedAt = DateTime.UtcNow
        };
    }
}
