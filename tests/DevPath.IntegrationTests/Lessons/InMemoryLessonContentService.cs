using DevPath.Application.Lessons;
using DevPath.Domain.Common;

namespace DevPath.IntegrationTests.Lessons;

/// <summary>
/// Dublê de teste do ILessonContentService (só nos testes da tela; o real é o LessonContentService
/// do Backend, registrado por AddInfrastructure). Imita as regras do serviço real:
/// - trilha inativa, módulo não publicado ou aula não publicada/inexistente = NotFound
///   (e a aula não publicada fica fora de anterior/próxima);
/// - rascunho aberto direto = Draft; rascunho na navegação = LessonLink.IsDraft;
/// - corpo vazio = Malformed;
/// - Position/TotalInModule contam as aulas publicadas do módulo (rascunhos incluídos);
/// - slugs comparados em minúsculas;
/// - o exercício (visão segura, ExerciseDto) vai em LessonDto.Exercise, como no real.
/// </summary>
public sealed class InMemoryLessonContentService : ILessonContentService
{
    public sealed record FakeLesson(
        string Slug,
        string Title,
        string Body,
        bool IsPublished = true,
        bool IsDraft = false,
        int EstimatedMinutes = 10,
        DifficultyLevel Level = DifficultyLevel.Beginner,
        string? Language = "sql",
        IReadOnlyList<string>? Objectives = null,
        ExerciseDto? Exercise = null)
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed record FakeModule(string Slug, string Title, IReadOnlyList<FakeLesson> Lessons, bool IsPublished = true);

    public sealed record FakePath(string Slug, string Title, IReadOnlyList<FakeModule> Modules, bool IsActive = true);

    private readonly IReadOnlyList<FakePath> _paths;

    public InMemoryLessonContentService(params FakePath[] paths) => _paths = paths;

    public Task<LessonLoadResult> GetLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
    {
        pathSlug = pathSlug.Trim().ToLowerInvariant();
        moduleSlug = moduleSlug.Trim().ToLowerInvariant();
        lessonSlug = lessonSlug.Trim().ToLowerInvariant();

        var path = _paths.SingleOrDefault(p => p.Slug == pathSlug && p.IsActive);
        var module = path?.Modules.SingleOrDefault(m => m.Slug == moduleSlug && m.IsPublished);
        var lesson = module?.Lessons.SingleOrDefault(l => l.Slug == lessonSlug && l.IsPublished);
        if (path is null || module is null || lesson is null)
            return Task.FromResult(LessonLoadResult.NotFound($"Aula não encontrada: /trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}."));

        if (lesson.IsDraft)
            return Task.FromResult(LessonLoadResult.Draft("Esta aula ainda está em construção."));

        if (string.IsNullOrWhiteSpace(lesson.Body))
            return Task.FromResult(LessonLoadResult.Malformed($"A aula \"{lesson.Title}\" está sem conteúdo."));

        var siblings = module.Lessons.Where(l => l.IsPublished).ToList();
        var index = siblings.IndexOf(lesson);
        LessonLink? Link(int i) => i >= 0 && i < siblings.Count ? new LessonLink(siblings[i].Slug, siblings[i].Title, siblings[i].IsDraft) : null;

        return Task.FromResult(LessonLoadResult.Success(new LessonDto(
            lesson.Id, lesson.Slug, lesson.Title, lesson.EstimatedMinutes, lesson.Level, lesson.Language,
            lesson.Objectives ?? [], lesson.Body,
            new LessonBreadcrumb(path.Slug, path.Title, module.Slug, module.Title),
            Link(index - 1), Link(index + 1), index + 1, siblings.Count, lesson.Exercise)));
    }
}
