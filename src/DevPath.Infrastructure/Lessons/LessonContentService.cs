using System.Text.Json;
using DevPath.Application.Lessons;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPath.Infrastructure.Lessons;

/// <inheritdoc />
/// <remarks>
/// Faz três consultas pequenas e só de leitura (<c>AsNoTracking</c>: o EF não guarda as entidades
/// para detectar mudanças, o que é mais leve):
/// 1) trilha + módulo + aula pelos slugs;
/// 2) a lista "enxuta" (slug, título, rascunho) das aulas publicadas do módulo, para montar
///    anterior/próxima e "Aula X de Y" sem carregar o Markdown de todas as aulas;
/// 3) o exercício da aula, projetado direto no <see cref="ExerciseDto"/> (sem solução nem casos de teste).
/// Se o jsonb de objetivos estiver corrompido no banco, a aula volta como <see cref="LessonLoadErrorKind.Malformed"/>
/// (com log) em vez de derrubar a página.
/// </remarks>
public sealed class LessonContentService(DevPathDbContext db, ILogger<LessonContentService>? logger = null) : ILessonContentService
{
    private readonly ILogger _logger = logger ?? NullLogger<LessonContentService>.Instance;

    public async Task<LessonLoadResult> GetLessonAsync(
        string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
    {
        // Os slugs no banco são sempre minúsculos; normalizar evita 404 por "/trilhas/Backend/...".
        pathSlug = (pathSlug ?? string.Empty).Trim().ToLowerInvariant();
        moduleSlug = (moduleSlug ?? string.Empty).Trim().ToLowerInvariant();
        lessonSlug = (lessonSlug ?? string.Empty).Trim().ToLowerInvariant();

        var visible =
            from path in db.CareerPaths.AsNoTracking()
            join module in db.Modules.AsNoTracking() on path.Id equals module.CareerPathId
            join lesson in db.Lessons.AsNoTracking() on module.Id equals lesson.ModuleId
            where path.Slug == pathSlug && path.IsActive
                  && module.Slug == moduleSlug && module.IsPublished
                  && lesson.Slug == lessonSlug && lesson.IsPublished
            select new { path, module, lesson };

        var route = $"/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}";

        var found = default(FoundLesson);
        try
        {
            found = await visible
                .Select(x => new FoundLesson(x.lesson, x.path.Title, x.module.Id, x.module.Title))
                .SingleOrDefaultAsync(ct);
        }
        catch (JsonException ex)
        {
            // O jsonb de LearningObjectives não é uma lista de textos (ex.: [1, null]). Relê sem essa coluna
            // para respeitar a ordem das verificações (não encontrada > rascunho > malformada).
            var header = await visible
                .Select(x => new { x.lesson.Title, x.lesson.IsDraft })
                .SingleOrDefaultAsync(ct);
            if (header is null)
            {
                return LessonLoadResult.NotFound($"Aula não encontrada: {route}.");
            }

            if (header.IsDraft)
            {
                return LessonLoadResult.Draft("Esta aula ainda está em construção.");
            }

            _logger.LogError(ex, "Aula {Route} tem LearningObjectives inválido no banco (não é uma lista de textos).", route);
            return LessonLoadResult.Malformed(
                $"A aula \"{header.Title}\" tem objetivos de aprendizagem inválidos no banco (esperado: lista de textos). "
                + "Verifique o arquivo em /content e rode o seed de novo.");
        }

        // Ordem das verificações: primeiro "existe?", depois "está pronta?", por fim "está íntegra?".
        if (found is null)
        {
            return LessonLoadResult.NotFound(
                $"Aula não encontrada: {route}.");
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

        // Exercício: projeção só com o que o aluno pode ver (nunca SolutionCode nem TestCases).
        // Um exercício por aula (um NN-aula.exercise.json); o mais antigo, se por acaso houver mais.
        ExerciseDto? exercise;
        try
        {
            exercise = await db.Exercises.AsNoTracking()
                .Where(e => e.LessonId == current.Id)
                .OrderBy(e => e.Id)
                .Select(e => new ExerciseDto(
                    e.Title, e.Statement, e.Language, e.Dataset,
                    e.StarterCode == "" ? null : e.StarterCode,
                    e.Hints, e.Points))
                .FirstOrDefaultAsync(ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Exercício da aula {Route} tem Hints inválido no banco (não é uma lista de textos).", route);
            return LessonLoadResult.Malformed(
                $"O exercício da aula \"{current.Title}\" tem dicas inválidas no banco (esperado: lista de textos). "
                + "Verifique o arquivo .exercise.json em /content e rode o seed de novo.");
        }

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
            TotalInModule: siblings.Count,
            Exercise: exercise);

        return LessonLoadResult.Success(dto);
    }

    private sealed record FoundLesson(Domain.CareerPaths.Lesson Lesson, string PathTitle, Guid ModuleId, string ModuleTitle);
}
