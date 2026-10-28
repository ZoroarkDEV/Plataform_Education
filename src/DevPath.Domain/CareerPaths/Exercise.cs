namespace DevPath.Domain.CareerPaths;

/// <summary>Um exercício de código corrigido automaticamente pelos casos de teste.</summary>
public sealed class Exercise
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public string Title { get; private set; } = string.Empty;

    /// <summary>Enunciado em Markdown.</summary>
    public string Statement { get; private set; } = string.Empty;

    /// <summary>Código inicial que aparece no editor do aluno.</summary>
    public string StarterCode { get; private set; } = string.Empty;

    /// <summary>Solução de referência. Nunca deve ser enviada ao aluno (use DTOs).</summary>
    public string SolutionCode { get; private set; } = string.Empty;

    /// <summary>Casos de teste em JSON (gravado como <c>jsonb</c> no PostgreSQL).</summary>
    public string TestCases { get; private set; } = "[]";

    public string Language { get; private set; } = string.Empty;

    /// <summary>Limite de tentativas; <c>null</c> significa ilimitado.</summary>
    public int? MaxAttempts { get; private set; }

    public int Points { get; private set; }

    /// <summary>Base de dados do exercício (<c>content/datasets/&lt;nome&gt;.sql</c>), ex.: "livraria". Opcional.</summary>
    public string? Dataset { get; private set; }

    /// <summary>Dicas na ordem do arquivo: da mais geral para a mais direta. Gravadas como jsonb.</summary>
    public IReadOnlyList<string> Hints { get; private set; } = [];

    public const int MaxDatasetLength = 100;

    private Exercise() { }

    public static Exercise Create(
        Guid lessonId,
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
        var exercise = new Exercise { Id = Guid.NewGuid(), LessonId = lessonId };
        exercise.Update(title, statement, language, testCasesJson, points, starterCode, solutionCode, maxAttempts,
            dataset, hints ?? []);
        return exercise;
    }

    /// <summary>Atualiza o exercício (ex.: o seed corrigindo um enunciado), com as mesmas validações do Create.</summary>
    /// <param name="dataset">Novo dataset (<c>null</c> = sem dataset).</param>
    /// <param name="hints">Novas dicas; <c>null</c> mantém as atuais.</param>
    public void Update(
        string title,
        string statement,
        string language,
        string testCasesJson,
        int points,
        string starterCode,
        string solutionCode,
        int? maxAttempts,
        string? dataset = null,
        IEnumerable<string>? hints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(testCasesJson);
        ArgumentNullException.ThrowIfNull(starterCode);
        ArgumentNullException.ThrowIfNull(solutionCode);
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        if (maxAttempts is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts.Value);
        }

        var normalizedDataset = string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim();
        if (normalizedDataset is { Length: > MaxDatasetLength })
        {
            throw new ArgumentException($"Dataset deve ter no máximo {MaxDatasetLength} caracteres.", nameof(dataset));
        }

        // Valida antes de alterar qualquer campo: ou atualiza tudo, ou nada.
        List<string>? normalizedHints = null;
        if (hints is not null)
        {
            normalizedHints = [];
            foreach (var hint in hints)
            {
                var trimmed = hint?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    throw new ArgumentException("Dica não pode ser vazia.", nameof(hints));
                }

                normalizedHints.Add(trimmed);
            }
        }

        Title = title.Trim();
        Statement = statement;
        Language = language.Trim().ToLowerInvariant();
        TestCases = testCasesJson;
        Points = points;
        StarterCode = starterCode;
        SolutionCode = solutionCode;
        MaxAttempts = maxAttempts;
        Dataset = normalizedDataset;
        Hints = normalizedHints ?? Hints;
    }
}
