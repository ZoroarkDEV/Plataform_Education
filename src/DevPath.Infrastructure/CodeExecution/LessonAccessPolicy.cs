using DevPath.Application.CodeExecution;
using DevPath.Application.Journey;
using DevPath.Infrastructure.Progress;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Execução de exercício (#27): visível, aula da trilha atual do aluno e liberada, pela mesma regra do dashboard.
/// </summary>
public sealed class LessonAccessPolicy(JourneySnapshotLoader loader) : ILessonAccessPolicy
{
    public async Task<ExerciseAccess> EvaluateAsync(Guid userId, Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await loader.FindLessonByExerciseAsync(exerciseId, cancellationToken);
        if (lesson is not { Visible: true })
        {
            return ExerciseAccess.NotFound;
        }

        var current = await loader.GetCurrentPathAsync(userId, cancellationToken);
        if (current.PathId != lesson.PathId)
        {
            return ExerciseAccess.NotEnrolled;
        }

        var snapshot = await loader.LoadAsync(userId, lesson.PathId, current.PathSlug!, current.PathTitle!, cancellationToken);
        return !snapshot.Contains(lesson.LessonId)
            ? ExerciseAccess.NotFound
            : snapshot.Lesson(lesson.LessonId).Status == JourneyLessonStatus.Locked
                ? ExerciseAccess.Locked
                : ExerciseAccess.Allowed;
    }
}
