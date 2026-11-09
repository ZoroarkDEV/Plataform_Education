using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.IntegrationTests.Progress;

/// <summary>Dublê configurável do ILessonProgressGateway para testes de tela (#27 / #30).</summary>
public sealed class FakeLessonProgressGateway : ILessonProgressGateway
{
    public Func<Guid, string, string, string, LessonAccessOutcome> Open { get; set; } =
        (_, _, _, _) => LessonAccessOutcome.Unavailable;

    public Func<Guid, string, string, string, LessonAccessOutcome> GetState { get; set; } =
        (_, _, _, _) => LessonAccessOutcome.Unavailable;

    public Func<Guid, string, string, string, LessonCompletionOutcome> Complete { get; set; } =
        (_, _, _, _) => new LessonCompletionOutcome(LessonCompletionStatus.Unavailable);

    public Func<Guid, string, string, string, LessonCompletionOutcome> Skip { get; set; } =
        (_, _, _, _) => new LessonCompletionOutcome(LessonCompletionStatus.Unavailable);

    public int CompleteCalls { get; private set; }
    public int SkipCalls { get; private set; }

    public Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        Task.FromResult(GetState(userId, pathSlug, moduleSlug, lessonSlug));

    public Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        Task.FromResult(Open(userId, pathSlug, moduleSlug, lessonSlug));

    public Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken)
    {
        CompleteCalls++;
        return Task.FromResult(Complete(userId, pathSlug, moduleSlug, lessonSlug));
    }

    public Task<LessonCompletionOutcome> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken)
    {
        SkipCalls++;
        return Task.FromResult(Skip(userId, pathSlug, moduleSlug, lessonSlug));
    }

    public static class Samples
    {
        public static readonly LessonRef Select = new("sql-dados", "primeiros-passos", "seu-primeiro-select", "Seu primeiro SELECT");
        public static readonly LessonRef Where = new("sql-dados", "primeiros-passos", "filtrando-com-where", "Filtrando com WHERE");
        public static readonly Guid ExerciseId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        public static LessonItemViewModel Available(LessonRef lesson) => new(lesson, LessonStatus.Available);
        public static LessonItemViewModel Completed(LessonRef lesson) => new(lesson, LessonStatus.Completed);
        public static LessonItemViewModel Locked(LessonRef lesson, LessonRef req) => new(lesson, LessonStatus.Locked, req);
        public static LessonItemViewModel ComingSoon(LessonRef lesson) => new(lesson, LessonStatus.ComingSoon);

        public static LessonProgressView ReadingAvailable(LessonItemViewModel? next = null) => new(
            Select, LessonStatus.Available, null, null, null, null,
            CanComplete: true, Blocker: null, Exercise: null,
            Previous: null, Next: next ?? Available(Where), PathProgressPercentage: 0);

        public static LessonProgressView ReadingCompleted() => new(
            Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
            LessonCompletionKind.Read, null, CanComplete: false, Blocker: null, Exercise: null,
            Previous: null, Next: Available(Where), PathProgressPercentage: 20);

        public static LessonProgressView ExerciseNotPassed(int failed = 0, bool canSkip = false, int? maxAttempts = null, int? remaining = null, int failuresBeforeSkip = 5) => new(
            Select, LessonStatus.Available, null, null, null, null,
            CanComplete: false, Blocker: LessonCompletionBlocker.ExerciseNotPassed,
            Exercise: new LessonExerciseProgressView(ExerciseId, 10, false, failed, maxAttempts, remaining, failed, canSkip,
                canSkip ? 0 : Math.Max(0, failuresBeforeSkip - failed)),
            Previous: null, Next: Locked(Where, Select), PathProgressPercentage: 0);

        public static LessonProgressView ExercisePassedCompleted(int points = 10) => new(
            Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
            LessonCompletionKind.ExercisePassed, points, CanComplete: false, Blocker: null,
            Exercise: new LessonExerciseProgressView(ExerciseId, points, true, 1, null, null, 0, false, null),
            Previous: null, Next: Available(Where), PathProgressPercentage: 20);

        public static LessonProgressView Skipped(int points = 10) => new(
            Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
            LessonCompletionKind.SkippedAfterFailures, 0, CanComplete: false, Blocker: null,
            Exercise: new LessonExerciseProgressView(ExerciseId, points, false, 5, null, null, 5, true, 0),
            Previous: null, Next: Available(Where), PathProgressPercentage: 20);

        /// <summary>MaxAttempts esgotado: Backend libera CanComplete; ainda não concluída; Próxima locked.</summary>
        public static LessonProgressView ExerciseAttemptsExhaustedReady(int maxAttempts = 3) => new(
            Select, LessonStatus.Available, null, null, null, null,
            CanComplete: true, Blocker: null,
            Exercise: new LessonExerciseProgressView(
                ExerciseId, 10, false, maxAttempts, maxAttempts, 0, maxAttempts, false, 0),
            Previous: null, Next: Locked(Where, Select), PathProgressPercentage: 0);

        public static LessonProgressView ExerciseAttemptsExhaustedCompleted() => new(
            Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
            LessonCompletionKind.AttemptsExhausted, 0, CanComplete: false, Blocker: null,
            Exercise: new LessonExerciseProgressView(ExerciseId, 10, false, 3, 3, 0, 3, false, 0),
            Previous: null, Next: Available(Where), PathProgressPercentage: 20);

        public static LessonAccessOutcome Allowed(LessonProgressView state) => new(LessonAccessStatus.Allowed, state);
        public static LessonAccessOutcome LockedState(LessonProgressView state) => new(LessonAccessStatus.Locked, state);
    }
}
