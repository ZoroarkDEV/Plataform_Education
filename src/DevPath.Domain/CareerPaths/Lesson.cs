using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>Uma aula: texto em Markdown, vídeo, conteúdo interativo, exercício ou projeto.</summary>
public sealed class Lesson
{
    private readonly List<Exercise> _exercises = [];

    public Guid Id { get; private set; }
    public Guid ModuleId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public ContentType ContentType { get; private set; }

    /// <summary>Markdown da aula ou caminho para o arquivo de conteúdo.</summary>
    public string Content { get; private set; } = string.Empty;

    public int Order { get; private set; }
    public int EstimatedMinutes { get; private set; }

    /// <summary>Linguagem dos exemplos (csharp, sql, javascript, python...). Opcional.</summary>
    public string? CodeLanguage { get; private set; }

    public DifficultyLevel Difficulty { get; private set; }

    public IReadOnlyCollection<Exercise> Exercises => _exercises;

    private Lesson() { }

    public static Lesson Create(
        Guid moduleId,
        string title,
        ContentType contentType,
        string content,
        int order,
        int estimatedMinutes = 0,
        string? codeLanguage = null,
        DifficultyLevel difficulty = DifficultyLevel.Beginner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedMinutes);

        return new Lesson
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            Title = title.Trim(),
            ContentType = contentType,
            Content = content,
            Order = order,
            EstimatedMinutes = estimatedMinutes,
            CodeLanguage = codeLanguage,
            Difficulty = difficulty
        };
    }

    /// <summary>Adiciona um exercício de código a esta aula.</summary>
    public Exercise AddExercise(string title, string statement, string language, string testCasesJson, int points = 10)
    {
        var exercise = Exercise.Create(Id, title, statement, language, testCasesJson, points);
        _exercises.Add(exercise);
        return exercise;
    }
}
