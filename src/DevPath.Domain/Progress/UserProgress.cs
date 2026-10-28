namespace DevPath.Domain.Progress;

/// <summary>
/// Progresso de um aluno em uma aula, exercício ou módulo (um dos três IDs é preenchido).
/// </summary>
/// <remarks>
/// <see cref="UserId"/> é só o ID por enquanto: o usuário vira <c>ApplicationUser</c>
/// (ASP.NET Identity) na issue #9, e aí ligamos a chave estrangeira.
/// </remarks>
public sealed class UserProgress
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    public Guid? LessonId { get; private set; }
    public Guid? ExerciseId { get; private set; }
    public Guid? ModuleId { get; private set; }

    public ProgressStatus Status { get; private set; }
    public int? Score { get; private set; }
    public int TimeSpentSeconds { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime LastAccessedAt { get; private set; }

    private UserProgress() { }

    public static UserProgress StartLesson(Guid userId, Guid lessonId) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LessonId = lessonId,
            Status = ProgressStatus.InProgress,
            LastAccessedAt = DateTime.UtcNow
        };

    /// <summary>Soma tempo de estudo e atualiza o último acesso.</summary>
    public void RegisterStudyTime(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        TimeSpentSeconds += seconds;
        LastAccessedAt = DateTime.UtcNow;
    }

    public void Complete(int? score = null)
    {
        Status = ProgressStatus.Completed;
        Score = score;
        CompletedAt = DateTime.UtcNow;
        LastAccessedAt = CompletedAt.Value;
    }
}
