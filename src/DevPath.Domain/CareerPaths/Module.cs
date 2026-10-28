using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>Um módulo de uma trilha (ex.: "Fundamentos de C#"), com aulas em ordem.</summary>
public sealed class Module
{
    private readonly List<Lesson> _lessons = [];

    public Guid Id { get; private set; }
    public Guid CareerPathId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<Lesson> Lessons => _lessons;

    private Module() { }

    public static Module Create(Guid careerPathId, string title, int order, string description = "", int estimatedMinutes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedMinutes);

        return new Module
        {
            Id = Guid.NewGuid(),
            CareerPathId = careerPathId,
            Title = title.Trim(),
            Description = description.Trim(),
            Order = order,
            EstimatedMinutes = estimatedMinutes,
            IsPublished = false // módulo nasce como rascunho
        };
    }

    public void Publish() => IsPublished = true;

    /// <summary>Adiciona uma aula no fim do módulo.</summary>
    public Lesson AddLesson(
        string title,
        ContentType contentType,
        string content,
        int estimatedMinutes = 0,
        string? codeLanguage = null,
        DifficultyLevel difficulty = DifficultyLevel.Beginner)
    {
        var lesson = Lesson.Create(Id, title, contentType, content, order: _lessons.Count + 1, estimatedMinutes, codeLanguage, difficulty);
        _lessons.Add(lesson);
        return lesson;
    }
}
