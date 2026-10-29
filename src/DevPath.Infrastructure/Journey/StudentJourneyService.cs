using DevPath.Application.Journey;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevPath.Infrastructure.Journey;

/// <inheritdoc />
/// <remarks>
/// Leituras com <c>AsNoTracking</c> e número fixo de consultas (sem N+1):
/// trilhas = 1 consulta (a contagem é subconsulta no SQL); dashboard = 3 (trilha atual, roadmap, progresso).
/// As regras de status/percentual ficam no <see cref="JourneyCalculator"/> (função pura).
/// </remarks>
public sealed class StudentJourneyService(DevPathDbContext db) : IStudentJourneyService
{
    public async Task<IReadOnlyList<PathSummaryDto>> GetActivePathsAsync(CancellationToken ct = default) =>
        await db.CareerPaths.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Order).ThenBy(p => p.Title)
            .Select(p => new PathSummaryDto(
                p.Slug,
                p.Title,
                p.Description,
                p.DifficultyLevel,
                p.EstimatedHours,
                db.Lessons.Count(l => l.IsPublished && !l.IsDraft
                    && db.Modules.Any(m => m.Id == l.ModuleId && m.CareerPathId == p.Id && m.IsPublished)),
                db.Lessons.Count(l => l.IsPublished
                    && db.Modules.Any(m => m.Id == l.ModuleId && m.CareerPathId == p.Id && m.IsPublished))))
            .ToListAsync(ct);

    public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.CurrentPathId != null, ct);

    public async Task<EnrollmentResult> EnrollAsync(Guid userId, string pathSlug, CancellationToken ct = default)
    {
        var slug = (pathSlug ?? string.Empty).Trim().ToLowerInvariant();

        var current = await CurrentPathAsync(userId, ct);
        if (current is not null)
        {
            return current;
        }

        var path = await db.CareerPaths.AsNoTracking()
            .Where(p => p.Slug == slug && p.IsActive)
            .Select(p => new { p.Id, p.Title })
            .SingleOrDefaultAsync(ct);
        if (path is null)
        {
            return EnrollmentResult.PathNotFound();
        }

        // Módulo inicial: o da 1ª aula disponível (publicada, de módulo publicado e não rascunho), o mesmo
        // critério do AvailableLessons. Trilha sem aula disponível, inclusive só com rascunhos, fica
        // "Em breve" e não aceita matrícula (R4).
        var firstModuleId = await (
                from m in db.Modules.AsNoTracking()
                join l in db.Lessons.AsNoTracking() on m.Id equals l.ModuleId
                where m.CareerPathId == path.Id && m.IsPublished && l.IsPublished && !l.IsDraft
                orderby m.Order, l.Order
                select (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (firstModuleId is null)
        {
            return EnrollmentResult.NoLessons();
        }

        // "Uma trilha por vez" é garantido por um UPDATE condicional (CurrentPathId IS NULL): no Postgres,
        // o 2º POST concorrente espera o lock da linha do 1º e, depois do commit, não casa mais o WHERE.
        // O índice único parcial (uma matrícula ativa por aluno) é a segunda trava, no banco.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var claimed = await db.Users
                .Where(u => u.Id == userId && u.CurrentPathId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CurrentPathId, path.Id), ct);

            if (claimed == 0)
            {
                // Outro POST matriculou primeiro (ou o usuário foi apagado no meio).
                await tx.RollbackAsync(ct);
                return await CurrentPathAsync(userId, ct) ?? EnrollmentResult.UserNotFound();
            }

            // Sem trilha atual não pode haver matrícula ativa (ex.: trilha antiga apagada deixou lixo).
            await db.UserPathEnrollments
                .Where(e => e.UserId == userId && e.IsActive && e.CareerPathId != path.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false), ct);

            var reactivated = await db.UserPathEnrollments
                .Where(e => e.UserId == userId && e.CareerPathId == path.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.IsActive, true)
                    .SetProperty(e => e.CurrentModuleId, firstModuleId), ct);

            if (reactivated == 0)
            {
                var enrollment = UserPathEnrollment.Enroll(userId, path.Id);
                enrollment.UpdateProgress(0, firstModuleId);
                db.UserPathEnrollments.Add(enrollment);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return EnrollmentResult.Enrolled(path.Title);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return await CurrentPathAsync(userId, ct) ?? EnrollmentResult.UserNotFound();
        }
    }

    /// <summary>
    /// UserNotFound se o usuário não existe; AlreadyEnrolled(título da trilha atual) se ele já tem trilha;
    /// null se ele existe e está livre para se matricular.
    /// </summary>
    private async Task<EnrollmentResult?> CurrentPathAsync(Guid userId, CancellationToken ct)
    {
        var user = await (
                from u in db.Users.AsNoTracking()
                where u.Id == userId
                select new
                {
                    u.CurrentPathId,
                    Title = db.CareerPaths.Where(p => p.Id == u.CurrentPathId).Select(p => p.Title).FirstOrDefault()
                })
            .SingleOrDefaultAsync(ct);

        if (user is null)
        {
            return EnrollmentResult.UserNotFound();
        }

        // CurrentPathId tem FK com SET NULL, então com Id preenchido o título sempre existe.
        return user.CurrentPathId is null ? null : EnrollmentResult.AlreadyEnrolled(user.Title ?? string.Empty);
    }

    public async Task<DashboardDto?> GetDashboardAsync(Guid userId, CancellationToken ct = default)
    {
        // A trilha atual aparece mesmo se for desativada depois: o progresso do aluno não some (R5).
        var path = await (
                from u in db.Users.AsNoTracking()
                join p in db.CareerPaths.AsNoTracking() on u.CurrentPathId equals (Guid?)p.Id
                where u.Id == userId
                select new { p.Id, p.Slug, p.Title })
            .SingleOrDefaultAsync(ct);
        if (path is null)
        {
            return null;
        }

        var rows = await (
                from m in db.Modules.AsNoTracking()
                join l in db.Lessons.AsNoTracking() on m.Id equals l.ModuleId
                where m.CareerPathId == path.Id && m.IsPublished && l.IsPublished
                orderby m.Order, m.Slug, l.Order, l.Slug
                select new
                {
                    ModuleId = m.Id,
                    ModuleSlug = m.Slug,
                    ModuleTitle = m.Title,
                    LessonId = l.Id,
                    LessonSlug = l.Slug,
                    LessonTitle = l.Title,
                    l.IsDraft
                })
            .ToListAsync(ct);

        // Progresso por aula da trilha. Se houver mais de uma linha para a mesma aula, vale o status mais avançado.
        var progressRows = await (
                from up in db.UserProgress.AsNoTracking()
                join l in db.Lessons.AsNoTracking() on up.LessonId equals (Guid?)l.Id
                join m in db.Modules.AsNoTracking() on l.ModuleId equals m.Id
                where up.UserId == userId && m.CareerPathId == path.Id
                select new { l.Id, up.Status })
            .ToListAsync(ct);

        var progress = progressRows
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Status));

        var modules = rows
            .GroupBy(r => r.ModuleId)
            .Select(g => new JourneyModuleInput(
                g.First().ModuleSlug,
                g.First().ModuleTitle,
                g.Select(r => new JourneyLessonInput(r.LessonId, r.LessonSlug, r.LessonTitle, r.IsDraft)).ToList()))
            .ToList();

        return JourneyCalculator.Build(path.Slug, path.Title, modules, progress);
    }
}
