using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Progress;

/// <summary>Trilha atual do aluno. <see cref="UserExists"/> false = conta apagada; PathId null = sem trilha.</summary>
public sealed record CurrentPathInfo(bool UserExists, Guid? PathId, string? PathSlug, string? PathTitle);

/// <summary>Uma aula encontrada pelos slugs ou pelo exercício, com o que decide se ela "existe" para o aluno.</summary>
public sealed record LessonLocation(Guid LessonId, Guid PathId, bool Visible);

/// <summary>
/// Carrega, com número fixo de consultas (sem N+1), o que o <see cref="JourneyCalculator"/> precisa (ADR-007).
/// Único lugar que lê roadmap + progresso: dashboard, progresso da aula e execução passam por aqui.
/// </summary>
/// <remarks>Dentro de uma transação aberta no <see cref="DevPathDbContext"/>, as consultas rodam nela.</remarks>
public sealed class JourneySnapshotLoader(DevPathDbContext db)
{
    public async Task<CurrentPathInfo> GetCurrentPathAsync(Guid userId, CancellationToken ct = default)
    {
        // A trilha atual vale mesmo se for desativada depois: o progresso do aluno não some (R5).
        var user = await (
                from u in db.Users.AsNoTracking()
                where u.Id == userId
                select new
                {
                    u.CurrentPathId,
                    Path = db.CareerPaths.Where(p => p.Id == u.CurrentPathId).Select(p => new { p.Slug, p.Title }).FirstOrDefault()
                })
            .SingleOrDefaultAsync(ct);

        return user is null
            ? new(false, null, null, null)
            : user.Path is null
                ? new(true, null, null, null)
                : new(true, user.CurrentPathId, user.Path.Slug, user.Path.Title);
    }

    /// <summary>Aula pelos slugs (comparados em minúsculas, sem espaços nas pontas). null = não existe.</summary>
    public Task<LessonLocation?> FindLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
    {
        static string Norm(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
        var (ps, ms, ls) = (Norm(pathSlug), Norm(moduleSlug), Norm(lessonSlug));

        return (
                from l in db.Lessons.AsNoTracking()
                join m in db.Modules.AsNoTracking() on l.ModuleId equals m.Id
                join p in db.CareerPaths.AsNoTracking() on m.CareerPathId equals p.Id
                where p.Slug == ps && m.Slug == ms && l.Slug == ls
                select new LessonLocation(l.Id, p.Id, p.IsActive && m.IsPublished && l.IsPublished && !l.IsDraft))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Aula do exercício. null = exercício não existe.</summary>
    public Task<LessonLocation?> FindLessonByExerciseAsync(Guid exerciseId, CancellationToken ct = default) =>
        (from e in db.Exercises.AsNoTracking()
         join l in db.Lessons.AsNoTracking() on e.LessonId equals l.Id
         join m in db.Modules.AsNoTracking() on l.ModuleId equals m.Id
         join p in db.CareerPaths.AsNoTracking() on m.CareerPathId equals p.Id
         where e.Id == exerciseId
         select new LessonLocation(l.Id, p.Id,
             p.IsActive && m.IsPublished && l.IsPublished && !l.IsDraft && e.IsPublished))
        .FirstOrDefaultAsync(ct);

    /// <summary>Roadmap + progresso do aluno na trilha: 2 consultas.</summary>
    public async Task<JourneySnapshot> LoadAsync(Guid userId, Guid pathId, string pathSlug, string pathTitle, CancellationToken ct = default)
    {
        var rows = await (
                from m in db.Modules.AsNoTracking()
                join l in db.Lessons.AsNoTracking() on m.Id equals l.ModuleId
                where m.CareerPathId == pathId && m.IsPublished && l.IsPublished
                orderby m.Order, m.Slug, l.Order, l.Slug
                select new JourneyLessonRow(l.Id, m.Id, m.Slug, m.Title, l.Slug, l.Title, l.IsDraft))
            .ToListAsync(ct);

        var progressRows = await (
                from up in db.UserProgress.AsNoTracking()
                join l in db.Lessons.AsNoTracking() on up.LessonId equals (Guid?)l.Id
                join m in db.Modules.AsNoTracking() on l.ModuleId equals m.Id
                where up.UserId == userId && m.CareerPathId == pathId
                select new LessonProgressRow(l.Id, up.Status, up.LastAccessedAt, up.CompletedAt, up.CompletionKind, up.Score))
            .ToListAsync(ct);

        // O índice único garante uma linha por aula; o GroupBy só protege contra dado antigo (vale o mais avançado).
        var progress = progressRows
            .GroupBy(r => r.LessonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Status).ThenByDescending(r => r.LastAccessedAt).First());

        var modules = rows
            .GroupBy(r => r.ModuleId)
            .Select(g => new JourneyModuleInput(
                g.First().ModuleSlug,
                g.First().ModuleTitle,
                g.Select(r => new JourneyLessonInput(r.LessonId, r.LessonSlug, r.LessonTitle, r.IsDraft)).ToList()))
            .ToList();

        var dashboard = JourneyCalculator.Build(pathSlug, pathTitle, modules,
            progress.ToDictionary(p => p.Key, p => new JourneyProgressInput(
                p.Value.Status, p.Value.LastAccessedAt, p.Value.CompletionKind, p.Value.Score)));

        return new JourneySnapshot(pathId, rows, progress, dashboard);
    }

    /// <summary>
    /// Os exercícios da aula (na mesma ordem da página: por Id) com as tentativas do aluno: 2 consultas.
    /// Lista vazia = aula só de leitura.
    /// </summary>
    public async Task<IReadOnlyList<ExerciseAttemptsInput>> LoadAttemptsAsync(Guid userId, Guid lessonId, CancellationToken ct = default)
    {
        var exercises = await db.Exercises.AsNoTracking()
            .Where(e => e.LessonId == lessonId)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.Points, e.MaxAttempts, e.FailuresBeforeSkip })
            .ToListAsync(ct);
        if (exercises.Count == 0)
        {
            return [];
        }

        var ids = exercises.Select(e => e.Id).ToList();
        var results = await db.CodeSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && ids.Contains(s.ExerciseId))
            .Select(s => new { s.ExerciseId, s.Result })
            .ToListAsync(ct);
        var byExercise = results.ToLookup(r => r.ExerciseId, r => r.Result);

        return exercises
            .Select(e =>
            {
                var counts = AttemptCounting.Count(byExercise[e.Id]);
                return new ExerciseAttemptsInput(e.Id, e.Points, e.MaxAttempts, e.FailuresBeforeSkip,
                    counts.Used, counts.Failed, counts.Passed);
            })
            .ToList();
    }
}
