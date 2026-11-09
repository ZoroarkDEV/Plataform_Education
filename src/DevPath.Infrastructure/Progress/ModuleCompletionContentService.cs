using DevPath.Application.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Progress;

/// <inheritdoc />
/// <remarks>
/// Uma consulta só de leitura: trilha ativa + módulo publicado + aulas publicadas e prontas,
/// com <c>CompletionSummary</c> preenchido, em ordem. Rascunhos e aulas despublicadas não entram.
/// </remarks>
public sealed class ModuleCompletionContentService(DevPathDbContext db) : IModuleCompletionContentService
{
    public async Task<ModuleCompletionContentDto?> GetAsync(
        string pathSlug, string moduleSlug, CancellationToken cancellationToken = default)
    {
        pathSlug = (pathSlug ?? string.Empty).Trim().ToLowerInvariant();
        moduleSlug = (moduleSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (pathSlug.Length == 0 || moduleSlug.Length == 0)
        {
            return null;
        }

        var module = await (
            from path in db.CareerPaths.AsNoTracking()
            join m in db.Modules.AsNoTracking() on path.Id equals m.CareerPathId
            where path.Slug == pathSlug && path.IsActive
                  && m.Slug == moduleSlug && m.IsPublished
            select new { m.Id, m.CompletionSubtitle }
        ).SingleOrDefaultAsync(cancellationToken);

        if (module is null)
        {
            return null;
        }

        var learned = await db.Lessons.AsNoTracking()
            .Where(l => l.ModuleId == module.Id
                        && l.IsPublished
                        && !l.IsDraft
                        && l.CompletionSummary != null
                        && l.CompletionSummary != "")
            .OrderBy(l => l.Order)
            .Select(l => new { l.Slug, Summary = l.CompletionSummary! })
            .ToListAsync(cancellationToken);

        // Dictionary preserva a ordem de inserção no .NET (e a UI itera o dashboard, não este mapa).
        var map = learned.ToDictionary(x => x.Slug, x => x.Summary, StringComparer.Ordinal);

        var subtitle = string.IsNullOrWhiteSpace(module.CompletionSubtitle)
            ? null
            : module.CompletionSubtitle.Trim();

        return new ModuleCompletionContentDto(subtitle, map);
    }
}
