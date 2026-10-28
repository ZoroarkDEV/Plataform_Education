using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>Uma aula: texto em Markdown, vídeo, conteúdo interativo, exercício ou projeto.</summary>
/// <remarks>O slug é único dentro do módulo e estável: o seed usa ele para achar a aula existente.</remarks>
public sealed class Lesson
{
    private readonly List<Exercise> _exercises = [];

    public Guid Id { get; private set; }
    public Guid ModuleId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public ContentType ContentType { get; private set; }

    /// <summary>Markdown da aula ou caminho para o arquivo de conteúdo.</summary>
    public string Content { get; private set; } = string.Empty;

    public int Order { get; private set; }
    public int EstimatedMinutes { get; private set; }

    /// <summary>Linguagem dos exemplos (csharp, sql, javascript, python...). Opcional.</summary>
    public string? CodeLanguage { get; private set; }

    public DifficultyLevel Difficulty { get; private set; }

    /// <summary>
    /// Aula visível para os alunos. <c>false</c> = removida do /content: continua no banco (o histórico
    /// de progresso aponta para ela), mas some da navegação e não abre. Nasce publicada.
    /// </summary>
    public bool IsPublished { get; private set; } = true;

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
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            Slug = SlugGenerator.Generate(title)
        };

        lesson.UpdateContent(title, contentType, content, order, estimatedMinutes, codeLanguage, difficulty);
        return lesson;
    }

    /// <summary>Atualiza o conteúdo da aula. O slug NÃO muda (use <see cref="ChangeSlug"/>).</summary>
    public void UpdateContent(
        string title,
        ContentType contentType,
        string content,
        int order,
        int estimatedMinutes,
        string? codeLanguage,
        DifficultyLevel difficulty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedMinutes);

        Title = title.Trim();
        ContentType = contentType;
        Content = content;
        Order = order;
        EstimatedMinutes = estimatedMinutes;
        CodeLanguage = codeLanguage;
        Difficulty = difficulty;
    }

    public void ChangeSlug(string slug) => Slug = SlugGenerator.Generate(slug);

    public void Publish() => IsPublished = true;
    public void Unpublish() => IsPublished = false;

    /// <summary>Adiciona um exercício de código a esta aula.</summary>
    public Exercise AddExercise(
        string title,
        string statement,
        string language,
        string testCasesJson,
        int points = 10,
        string starterCode = "",
        string solutionCode = "",
        int? maxAttempts = null)
    {
        var exercise = Exercise.Create(Id, title, statement, language, testCasesJson, points, starterCode, solutionCode, maxAttempts);
        _exercises.Add(exercise);
        return exercise;
    }
}
