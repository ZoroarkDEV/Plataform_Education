using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>Lê, numa consulta só, a trilha/módulo/aula de um exercício para a regra de acesso.</summary>
public sealed class ExerciseCatalog(DevPathDbContext db) : IExerciseCatalog
{
    public Task<ExerciseVisibility?> GetVisibilityAsync(Guid exerciseId, CancellationToken cancellationToken = default) =>
        (from exercise in db.Exercises.AsNoTracking()
         join lesson in db.Lessons.AsNoTracking() on exercise.LessonId equals lesson.Id
         join module in db.Modules.AsNoTracking() on lesson.ModuleId equals module.Id
         join path in db.CareerPaths.AsNoTracking() on module.CareerPathId equals path.Id
         where exercise.Id == exerciseId
         select new ExerciseVisibility(exercise.Id, lesson.Id, path.IsActive, module.IsPublished,
             lesson.IsPublished, lesson.IsDraft, exercise.IsPublished))
        .SingleOrDefaultAsync(cancellationToken);
}
