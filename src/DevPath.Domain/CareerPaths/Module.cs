using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>Um módulo de uma trilha (ex.: "Fundamentos de C#"), com aulas em ordem.</summary>
/// <remarks>O slug é único dentro da trilha e estável: o seed usa ele para achar o módulo existente.</remarks>
public sealed class Module
{
    private readonly List<Lesson> _lessons = [];

    public Guid Id { get; private set; }
    public Guid CareerPathId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<Lesson> Lessons => _lessons;

    private Module() { }

    public static Module Create(Guid careerPathId, string title, int order, string description = "", int estimatedMinutes = 0)
    {
        var module = new Module
        {
            Id = Guid.NewGuid(),
            CareerPathId = careerPathId,
            Slug = SlugGenerator.Generate(title),
            IsPublished = false // módulo nasce como rascunho
        };

        module.UpdateDetails(title, description, order, estimatedMinutes);
        return module;
    }

    /// <summary>Atualiza os dados do módulo. O slug NÃO muda (use <see cref="ChangeSlug"/>).</summary>
    public void UpdateDetails(string title, string description, int order, int estimatedMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedMinutes);

        Title = title.Trim();
        Description = description.Trim();
        Order = order;
        EstimatedMinutes = estimatedMinutes;
    }

    public void ChangeSlug(string slug) => Slug = SlugGenerator.Generate(slug);

    public void Publish() => IsPublished = true;
    public void Unpublish() => IsPublished = false;

    /// <summary>Adiciona uma aula no fim do módulo.</summary>
    public Lesson AddLesson(
        string title,
        ContentType contentType,
        string content,
        int estimatedMinutes = 0,
        string? codeLanguage = null,
        DifficultyLevel difficulty = DifficultyLevel.Beginner,
        IEnumerable<string>? learningObjectives = null,
        bool isDraft = false)
    {
        var lesson = Lesson.Create(Id, title, contentType, content, order: _lessons.Count + 1, estimatedMinutes, codeLanguage,
            difficulty, learningObjectives, isDraft);
        _lessons.Add(lesson);
        return lesson;
    }
}
