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

    /// <summary>
    /// O que o aluno vai saber fazer ao fim da aula (lista "objectives:" do frontmatter).
    /// Lista vazia quando a aula não define objetivos. Gravada como jsonb.
    /// </summary>
    public IReadOnlyList<string> LearningObjectives { get; private set; } = [];

    /// <summary>Aula em construção: aparece na navegação como "Em breve", mas não abre.</summary>
    public bool IsDraft { get; private set; }

    /// <summary>Tamanho máximo de cada objetivo de aprendizagem.</summary>
    public const int MaxObjectiveLength = 200;

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
        DifficultyLevel difficulty = DifficultyLevel.Beginner,
        IEnumerable<string>? learningObjectives = null,
        bool isDraft = false)
    {
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            Slug = SlugGenerator.Generate(title)
        };

        lesson.UpdateContent(title, contentType, content, order, estimatedMinutes, codeLanguage, difficulty,
            learningObjectives ?? [], isDraft);
        return lesson;
    }

    /// <summary>Atualiza o conteúdo da aula. O slug NÃO muda (use <see cref="ChangeSlug"/>).</summary>
    /// <param name="learningObjectives">Nova lista de objetivos; <c>null</c> mantém a atual.</param>
    /// <param name="isDraft">Novo estado de rascunho; <c>null</c> mantém o atual.</param>
    /// <remarks>
    /// Os dois últimos parâmetros são "nulo = não mexe" de propósito: quem só quer mudar a ordem
    /// (ex.: o seed reordenando em dois passos) não apaga os objetivos nem o rascunho sem querer.
    /// </remarks>
    public void UpdateContent(
        string title,
        ContentType contentType,
        string content,
        int order,
        int estimatedMinutes,
        string? codeLanguage,
        DifficultyLevel difficulty,
        IEnumerable<string>? learningObjectives = null,
        bool? isDraft = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedMinutes);
        // Valida antes de alterar qualquer campo: ou atualiza tudo, ou nada.
        var objectives = learningObjectives is null ? null : NormalizeObjectives(learningObjectives);

        Title = title.Trim();
        ContentType = contentType;
        Content = content;
        Order = order;
        EstimatedMinutes = estimatedMinutes;
        CodeLanguage = codeLanguage;
        Difficulty = difficulty;
        LearningObjectives = objectives ?? LearningObjectives;
        IsDraft = isDraft ?? IsDraft;
    }

    public void MarkAsDraft() => IsDraft = true;
    public void MarkAsReady() => IsDraft = false;

    // Cada objetivo: sem espaços nas pontas, não vazio e com até 200 caracteres.
    private static List<string> NormalizeObjectives(IEnumerable<string> objectives)
    {
        var normalized = new List<string>();
        foreach (var objective in objectives)
        {
            var trimmed = objective?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                throw new ArgumentException("Objetivo de aprendizagem não pode ser vazio.", "learningObjectives");
            }

            if (trimmed.Length > MaxObjectiveLength)
            {
                throw new ArgumentException(
                    $"Objetivo de aprendizagem deve ter no máximo {MaxObjectiveLength} caracteres.", "learningObjectives");
            }

            normalized.Add(trimmed);
        }

        return normalized;
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
        int? maxAttempts = null,
        string? dataset = null,
        IEnumerable<string>? hints = null)
    {
        var exercise = Exercise.Create(Id, title, statement, language, testCasesJson, points, starterCode, solutionCode, maxAttempts,
            dataset, hints);
        _exercises.Add(exercise);
        return exercise;
    }
}
