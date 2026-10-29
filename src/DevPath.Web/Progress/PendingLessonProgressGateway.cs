namespace DevPath.Web.Progress;

/// <summary>
/// Registrado enquanto o <c>ILessonProgressService</c> do Backend (#27, ADR-007) não chega na <c>dev</c>.
/// Tudo <c>Unavailable</c>: a aula abre como antes do #27 (sem bloqueio, "Próxima" liberado, sem selo) e o
/// "Concluí esta aula" não aparece. Mantém a <c>dev</c> publicável. Troque pelo adapter do serviço real em <c>Program.cs</c>.
/// </summary>
public sealed class PendingLessonProgressGateway : ILessonProgressGateway
{
    public Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        Task.FromResult(LessonAccessOutcome.Unavailable);

    public Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        Task.FromResult(LessonAccessOutcome.Unavailable);

    public Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, bool skipWithoutPoints, CancellationToken cancellationToken) =>
        Task.FromResult(new LessonCompletionOutcome(LessonCompletionStatus.Unavailable));
}
