using DevPath.Application.Lessons;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Lessons;

/// <inheritdoc />
/// <remarks>
/// Faz duas consultas pequenas e só de leitura (<c>AsNoTracking</c>: o EF não guarda as entidades
/// para detectar mudanças, o que é mais leve):
/// 1) trilha + módulo + aula pelos slugs;
/// 2) a lista "enxuta" (slug, título, rascunho) das aulas publicadas do módulo, para montar
///    anterior/próxima e "Aula X de Y" sem carregar o Markdown de todas as aulas.
/// </remarks>
public sealed class LessonContentService(DevPathDbContext db) : ILessonContentService
{
    public async Task<LessonLoadResult> GetLessonAsync(
        string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
    {
        // Os slugs no banco são sempre minúsculos; normalizar evita 404 por "/trilhas/Backend/...".
        pathSlug = (pathSlug ?? string.Empty).Trim().ToLowerInvariant();
        moduleSlug = (moduleSlug ?? string.Empty).Trim().ToLowerInvariant();
        lessonSlug = (lessonSlug ?? string.Empty).Trim().ToLowerInvariant();

        var found = await (
                from path in db.CareerPaths.AsNoTracking()
                join module in db.Modules.AsNoTracking() on path.Id equals module.CareerPathId
                join lesson in db.Lessons.AsNoTracking() on module.Id equals lesson.ModuleId
                where path.Slug == pathSlug && path.IsActive
                      && module.Slug == moduleSlug && module.IsPublished
                      && lesson.Slug == lessonSlug && lesson.IsPublished
                select new
                {
                    Lesson = lesson,
                    PathTitle = path.Title,
                    ModuleId = module.Id,
                    ModuleTitle = module.Title
                })
            .SingleOrDefaultAsync(ct);

        // Ordem das verificações: primeiro "existe?", depois "está pronta?", por fim "está íntegra?".
        if (found is null)
        {
            return LessonLoadResult.NotFound(
                $"Aula não encontrada: /trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}.");
        }

        var current = found.Lesson;

        if (current.IsDraft)
        {
            return LessonLoadResult.Draft("Esta aula ainda está em construção.");
        }

        if (string.IsNullOrWhiteSpace(current.Content))
        {
            return LessonLoadResult.Malformed(
                $"A aula \"{current.Title}\" está sem conteúdo. Verifique o arquivo em /content.");
        }

        // Navegação: só aulas publicadas do mesmo módulo, rascunhos incluídos, na ordem do módulo.
        var siblings = await db.Lessons.AsNoTracking()
            .Where(l => l.ModuleId == found.ModuleId && l.IsPublished)
            .OrderBy(l => l.Order)
            .Select(l => new { l.Id, l.Slug, l.Title, l.IsDraft })
            .ToListAsync(ct);

        var index = siblings.FindIndex(s => s.Id == current.Id);
        LessonLink? ToLink(int i) =>
            i >= 0 && i < siblings.Count ? new LessonLink(siblings[i].Slug, siblings[i].Title, siblings[i].IsDraft) : null;

        var dto = new LessonDto(
            LessonId: current.Id,
            Slug: current.Slug,
            Title: current.Title,
            EstimatedMinutes: current.EstimatedMinutes,
            Level: current.Difficulty,
            Language: current.CodeLanguage,
            LearningObjectives: current.LearningObjectives,
            MarkdownBody: current.Content,
            Breadcrumb: new LessonBreadcrumb(pathSlug, found.PathTitle, moduleSlug, found.ModuleTitle),
            PreviousLesson: ToLink(index - 1),
            NextLesson: ToLink(index + 1),
            Position: index + 1,
            TotalInModule: siblings.Count);

        return LessonLoadResult.Success(dto);
    }
}
