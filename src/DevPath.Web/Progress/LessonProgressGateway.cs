using DevPath.Application.Progress;
using DevPath.Web.Onboarding;
using DomainKind = DevPath.Domain.Progress.LessonCompletionKind;
using AppBlocker = DevPath.Application.Progress.LessonCompletionBlocker;

namespace DevPath.Web.Progress;

/// <summary>
/// Adapter da porta da Web (<see cref="ILessonProgressGateway"/>) para o serviço do Backend
/// (<see cref="ILessonProgressService"/>, #27, ADR-007).
/// </summary>
/// <remarks>
/// Só traduz, como o <see cref="StudentJourneyGateway"/>: DTOs campo a campo e enums com <c>switch</c> explícito
/// (nunca cast), para que um membro novo no Backend quebre os testes em vez de virar o estado errado na tela.
/// Sem catch: o serviço não lança nos casos esperados; exceção é falha de verdade e sobe para o controller.
/// "Concluí" → <see cref="ILessonProgressService.CompleteAsync"/>; "Seguir sem pontos" →
/// <see cref="ILessonProgressService.SkipAsync"/> (CompleteAsync nunca grava skip).
/// </remarks>
public sealed class LessonProgressGateway(ILessonProgressService service, ILogger<LessonProgressGateway> logger)
    : ILessonProgressGateway
{
    public async Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        ToOutcome(await service.GetStateAsync(userId, pathSlug, moduleSlug, lessonSlug, cancellationToken));

    public async Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        ToOutcome(await service.OpenAsync(userId, pathSlug, moduleSlug, lessonSlug, cancellationToken));

    public async Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        ToOutcome(await service.CompleteAsync(userId, pathSlug, moduleSlug, lessonSlug, cancellationToken));

    public async Task<LessonCompletionOutcome> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken)
    {
        var result = await service.SkipAsync(userId, pathSlug, moduleSlug, lessonSlug, cancellationToken);
        if (result.Kind == LessonCompletionResultKind.Completed
            && result.State?.CompletionKind is DomainKind.SkippedAfterFailures)
        {
            logger.LogInformation("\"Seguir sem pontos\" de {UserId} concluiu a aula.", userId);
        }
        return ToOutcome(result);
    }

    public static LessonAccessOutcome ToOutcome(LessonAccessResult result) => new(
        ToStatus(result.Kind),
        result.State is null ? null : ToView(result.State),
        result.CurrentPathTitle);

    public static LessonCompletionOutcome ToOutcome(LessonCompletionResult result) => new(
        ToStatus(result.Kind),
        result.State is null ? null : ToView(result.State));

    public static LessonAccessStatus ToStatus(LessonAccessKind kind) => kind switch
    {
        LessonAccessKind.Allowed => LessonAccessStatus.Allowed,
        LessonAccessKind.Locked => LessonAccessStatus.Locked,
        LessonAccessKind.NotEnrolled => LessonAccessStatus.NotEnrolled,
        LessonAccessKind.LessonNotFound => LessonAccessStatus.LessonNotFound,
        LessonAccessKind.UserNotFound => LessonAccessStatus.UserNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "LessonAccessKind sem mapeamento na Web.")
    };

    public static LessonCompletionStatus ToStatus(LessonCompletionResultKind kind) => kind switch
    {
        LessonCompletionResultKind.Completed => LessonCompletionStatus.Completed,
        LessonCompletionResultKind.AlreadyCompleted => LessonCompletionStatus.AlreadyCompleted,
        LessonCompletionResultKind.RequirementNotMet => LessonCompletionStatus.RequirementNotMet,
        LessonCompletionResultKind.Locked => LessonCompletionStatus.Locked,
        LessonCompletionResultKind.NotEnrolled => LessonCompletionStatus.NotEnrolled,
        LessonCompletionResultKind.LessonNotFound => LessonCompletionStatus.LessonNotFound,
        LessonCompletionResultKind.UserNotFound => LessonCompletionStatus.UserNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "LessonCompletionResultKind sem mapeamento na Web.")
    };

    public static LessonCompletionKind ToView(DomainKind kind) => kind switch
    {
        DomainKind.Read => LessonCompletionKind.Read,
        DomainKind.ExercisePassed => LessonCompletionKind.ExercisePassed,
        DomainKind.AttemptsExhausted => LessonCompletionKind.AttemptsExhausted,
        DomainKind.SkippedAfterFailures => LessonCompletionKind.SkippedAfterFailures,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "LessonCompletionKind sem mapeamento na Web.")
    };

    public static LessonCompletionBlocker ToView(AppBlocker blocker) => blocker switch
    {
        AppBlocker.ExerciseNotPassed => LessonCompletionBlocker.ExerciseNotPassed,
        _ => throw new ArgumentOutOfRangeException(nameof(blocker), blocker, "LessonCompletionBlocker sem mapeamento na Web.")
    };

    /// <summary>Só progresso: o DTO do Backend não tem solução, casos de teste nem erros comuns.</summary>
    public static LessonExerciseProgressView ToView(LessonExerciseProgressDto dto) => new(
        dto.ExerciseId, dto.Points, dto.Passed, dto.AttemptsUsed, dto.MaxAttempts, dto.RemainingAttempts,
        dto.FailedAttempts, dto.CanSkip, dto.FailuresUntilSkip);

    public static LessonProgressView ToView(LessonProgressState s) => new(
        StudentJourneyGateway.ToViewModel(s.Lesson),
        StudentJourneyGateway.ToViewModel(s.Status),
        s.UnlockRequirement is null ? null : StudentJourneyGateway.ToViewModel(s.UnlockRequirement),
        s.CompletedAt,
        s.CompletionKind is { } k ? ToView(k) : null,
        s.Points,
        s.CanComplete,
        s.Blocker is { } b ? ToView(b) : null,
        s.Exercise is null ? null : ToView(s.Exercise),
        s.Previous is null ? null : StudentJourneyGateway.ToViewModel(s.Previous),
        s.Next is null ? null : StudentJourneyGateway.ToViewModel(s.Next),
        s.PathProgressPercentage);
    // PathTotalPoints fica fora de propósito (UX I6, trabalho posterior).
}
