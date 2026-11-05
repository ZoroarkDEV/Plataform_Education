using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Infrastructure.Persistence;

namespace DevPath.Infrastructure.Progress;

/// <inheritdoc />
/// <remarks>
/// Ordem das verificações (ADR-007): UserNotFound &gt; LessonNotFound &gt; NotEnrolled &gt; Locked &gt; Allowed.
/// O status vem sempre do <see cref="JourneyCalculator"/> (via <see cref="JourneySnapshotLoader"/>), a regra do dashboard.
/// </remarks>
public sealed class LessonProgressService(DevPathDbContext db, JourneySnapshotLoader loader, LessonProgressWriter writer)
    : ILessonProgressService
{
    private sealed record Resolved(LessonAccessResult? Denied, Guid LessonId, CurrentPathInfo Path);

    public async Task<LessonAccessResult> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        return resolved.Denied ?? await AccessAsync(userId, resolved, ct);
    }

    public async Task<LessonAccessResult> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        if (resolved.Denied is not null)
        {
            return resolved.Denied;
        }

        var access = await AccessAsync(userId, resolved, ct);
        if (access.Kind != LessonAccessKind.Allowed)
        {
            return access; // bloqueada não grava nada
        }

        await writer.MarkOpenedAsync(userId, resolved.LessonId, ct);
        return access.State!.Status == JourneyLessonStatus.Available
            ? await AccessAsync(userId, resolved, ct) // virou InProgress: o "Continuar" e o estado mudam
            : access;
    }

    public Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug,
        CancellationToken ct = default) =>
        FinishAsync(userId, pathSlug, moduleSlug, lessonSlug, skipWithoutPoints: false, ct);

    public Task<LessonCompletionResult> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug,
        CancellationToken ct = default) =>
        FinishAsync(userId, pathSlug, moduleSlug, lessonSlug, skipWithoutPoints: true, ct);

    private async Task<LessonCompletionResult> FinishAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug,
        bool skipWithoutPoints, CancellationToken ct)
    {
        var resolved = await ResolveAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        if (resolved.Denied is { } denied)
        {
            return new(denied.Kind switch
            {
                LessonAccessKind.UserNotFound => LessonCompletionResultKind.UserNotFound,
                LessonAccessKind.NotEnrolled => LessonCompletionResultKind.NotEnrolled,
                _ => LessonCompletionResultKind.LessonNotFound
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Fila por aluno: o 2º clique espera o 1º terminar e então vê a aula concluída (AlreadyCompleted).
        if (!await writer.LockEnrollmentAsync(userId, ct))
        {
            return new(LessonCompletionResultKind.NotEnrolled);
        }

        var snapshot = await LoadAsync(userId, resolved.Path, ct);
        if (!snapshot.Contains(resolved.LessonId))
        {
            return new(LessonCompletionResultKind.LessonNotFound);
        }

        var attempts = await loader.LoadAttemptsAsync(userId, resolved.LessonId, ct);
        var before = BuildState(snapshot, resolved.LessonId, attempts);
        switch (before.Status)
        {
            case JourneyLessonStatus.Locked:
                return new(LessonCompletionResultKind.Locked, before);
            case JourneyLessonStatus.Completed:
                return new(LessonCompletionResultKind.AlreadyCompleted, before);
        }

        var decision = skipWithoutPoints
            ? LessonCompletionRule.EvaluateSkip(attempts)
            : LessonCompletionRule.Evaluate(attempts);
        if (!decision.CanComplete)
        {
            return new(LessonCompletionResultKind.RequirementNotMet, before);
        }

        await writer.MarkCompletedAsync(userId, resolved.LessonId, decision.Kind!.Value, decision.Points, ct);
        var after = await LoadAsync(userId, resolved.Path, ct);
        await writer.RecomputeEnrollmentAsync(userId, after, ct);
        await tx.CommitAsync(ct);

        return new(LessonCompletionResultKind.Completed, BuildState(after, resolved.LessonId, attempts));
    }

    private async Task<Resolved> ResolveAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct)
    {
        var current = await loader.GetCurrentPathAsync(userId, ct);
        if (!current.UserExists)
        {
            return new(LessonAccessResult.UserNotFound(), default, current);
        }

        var lesson = await loader.FindLessonAsync(pathSlug, moduleSlug, lessonSlug, ct);
        if (lesson is not { Visible: true })
        {
            return new(LessonAccessResult.NotFound(), default, current);
        }

        return lesson.PathId != current.PathId
            ? new(LessonAccessResult.NotEnrolled(current.PathTitle), lesson.LessonId, current)
            : new(null, lesson.LessonId, current);
    }

    private Task<JourneySnapshot> LoadAsync(Guid userId, CurrentPathInfo path, CancellationToken ct) =>
        loader.LoadAsync(userId, path.PathId!.Value, path.PathSlug!, path.PathTitle!, ct);

    private async Task<LessonAccessResult> AccessAsync(Guid userId, Resolved resolved, CancellationToken ct)
    {
        var snapshot = await LoadAsync(userId, resolved.Path, ct);
        if (!snapshot.Contains(resolved.LessonId))
        {
            return LessonAccessResult.NotFound();
        }

        var state = BuildState(snapshot, resolved.LessonId, await loader.LoadAttemptsAsync(userId, resolved.LessonId, ct));
        return new(state.Status == JourneyLessonStatus.Locked ? LessonAccessKind.Locked : LessonAccessKind.Allowed, state);
    }

    /// <summary>Monta o estado da aula a partir da foto da trilha e das tentativas (sem banco).</summary>
    internal static LessonProgressState BuildState(JourneySnapshot snapshot, Guid lessonId, IReadOnlyList<ExerciseAttemptsInput> attempts)
    {
        var lesson = snapshot.Lesson(lessonId);
        snapshot.Progress.TryGetValue(lessonId, out var row);
        var completed = lesson.Status == JourneyLessonStatus.Completed;
        var open = lesson.Status is JourneyLessonStatus.InProgress or JourneyLessonStatus.Available;
        var decision = LessonCompletionRule.Evaluate(attempts);

        return new LessonProgressState(
            lesson.Lesson,
            lesson.Status,
            lesson.UnlockRequirement,
            completed && row?.CompletedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            completed ? row?.CompletionKind : null,
            completed ? row?.Score : null,
            CanComplete: open && decision.CanComplete,
            Blocker: open ? decision.Blocker : null,
            attempts.Count > 0 ? LessonCompletionRule.Describe(attempts[0]) : null,
            snapshot.Previous(lessonId),
            snapshot.Next(lessonId),
            snapshot.Dashboard.ProgressPercentage,
            snapshot.Dashboard.TotalPoints);
    }
}
