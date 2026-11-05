using System.Collections.Concurrent;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Demo;

/// <summary>
/// Modo demo (só Development + <c>Demo:FakeLessonProgress=true</c>): progresso de aula em memória para revisar
/// as telas da #27 sem Backend/Worker. Compilado sempre, registrado só sob a flag (ver Program.cs e docs/frontend.md).
/// Cenário inicial por <c>Demo:LessonScenario</c>: <c>reading</c> (padrão), <c>exercise</c>, <c>skip</c>,
/// <c>exhausted</c>, <c>skipped</c>. Nunca sai solução/test_cases: só monta <see cref="LessonProgressView"/>.
/// </summary>
public sealed class FakeLessonProgressGateway(IConfiguration configuration) : ILessonProgressGateway
{
    private readonly ConcurrentDictionary<string, LessonProgressView> _states = new();
    private string Scenario => (configuration["Demo:LessonScenario"] ?? "reading").ToLowerInvariant();

    public Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        Task.FromResult(new LessonAccessOutcome(LessonAccessStatus.Allowed, StateFor(pathSlug, moduleSlug, lessonSlug)));

    public Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
        GetStateAsync(userId, pathSlug, moduleSlug, lessonSlug, cancellationToken);

    public Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, bool skipWithoutPoints, CancellationToken cancellationToken)
    {
        var key = Key(pathSlug, moduleSlug, lessonSlug);
        var current = StateFor(pathSlug, moduleSlug, lessonSlug);
        if (current.IsCompleted)
            return Task.FromResult(new LessonCompletionOutcome(LessonCompletionStatus.AlreadyCompleted, current));

        var skip = current.Exercise is { CanSkip: true };
        if (!current.CanComplete && !skip)
            return Task.FromResult(new LessonCompletionOutcome(LessonCompletionStatus.RequirementNotMet, current));

        var done = current with
        {
            Status = LessonStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            CompletionKind = current.Exercise is null ? LessonCompletionKind.Read : LessonCompletionKind.SkippedAfterFailures,
            Points = current.Exercise is null ? null : 0,
            CanComplete = false,
            Blocker = null,
            Next = current.Next is { } n ? n with { Status = LessonStatus.Available, UnlockRequirement = null } : null,
            PathProgressPercentage = 20,
        };
        _states[key] = done;
        return Task.FromResult(new LessonCompletionOutcome(LessonCompletionStatus.Completed, done));
    }

    private static string Key(string p, string m, string l) => $"{p}/{m}/{l}";

    private LessonProgressView StateFor(string pathSlug, string moduleSlug, string lessonSlug) =>
        _states.GetOrAdd(Key(pathSlug, moduleSlug, lessonSlug), _ => Initial(pathSlug, moduleSlug, lessonSlug));

    private LessonProgressView Initial(string pathSlug, string moduleSlug, string lessonSlug)
    {
        var lesson = new LessonRef(pathSlug, moduleSlug, lessonSlug, "Aula atual (demo)");
        var next = new LessonRef(pathSlug, moduleSlug, "proxima-aula-demo", "Próxima aula (demo)");
        var lockedNext = new LessonItemViewModel(next, LessonStatus.Locked, lesson);
        var exerciseId = Guid.Parse("00000000-0000-0000-0000-00000000de30");

        LessonProgressView Exercise(int failed, bool canSkip, int? max, int? remaining) => new(
            lesson, LessonStatus.Available, null, null, null, null,
            CanComplete: false, Blocker: LessonCompletionBlocker.ExerciseNotPassed,
            Exercise: new LessonExerciseProgressView(exerciseId, 10, false, failed, max, remaining, failed, canSkip,
                canSkip ? 0 : Math.Max(0, 5 - failed)),
            Previous: null, Next: lockedNext, PathProgressPercentage: 0);

        return Scenario switch
        {
            "exercise" => Exercise(0, false, null, null),
            "skip" => Exercise(5, true, null, null),
            "exhausted" => Exercise(3, false, 3, 0) with
            {
                Status = LessonStatus.Completed, CompletedAt = DateTimeOffset.UtcNow,
                CompletionKind = LessonCompletionKind.AttemptsExhausted, Points = 0, Blocker = null,
                Next = lockedNext with { Status = LessonStatus.Available, UnlockRequirement = null },
            },
            "skipped" => Exercise(5, true, null, null) with
            {
                Status = LessonStatus.Completed, CompletedAt = DateTimeOffset.UtcNow,
                CompletionKind = LessonCompletionKind.SkippedAfterFailures, Points = 0, Blocker = null,
                Next = lockedNext with { Status = LessonStatus.Available, UnlockRequirement = null },
            },
            _ => new LessonProgressView(lesson, LessonStatus.Available, null, null, null, null,
                CanComplete: true, Blocker: null, Exercise: null, Previous: null, Next: lockedNext, PathProgressPercentage: 0),
        };
    }
}
