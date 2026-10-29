using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace DevPath.Application.Content;

/// <summary>
/// Regras de consistência do conteúdo (SPEC §9.4), checadas antes de qualquer gravação no banco.
/// </summary>
/// <remarks>
/// O formato dos arquivos (YAML, frontmatter, campos obrigatórios) é checado por quem lê os arquivos.
/// Aqui ficam as regras entre itens (ordem, slugs, módulos sem aula) e os tamanhos máximos,
/// para o erro apontar o arquivo em vez de estourar no banco.
/// </remarks>
public static partial class ContentValidator
{
    // Mesmos limites das configurations do EF Core (DevPath.Infrastructure/Persistence/Configurations).
    public const int MaxTitleLength = 200;
    public const int MaxSlugLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxIconLength = 100;
    public const int MaxLanguageLength = 50;

    /// <summary>Cada item de <c>objectives</c> (combinado com o Backend para <c>Lesson.LearningObjectives</c>).</summary>
    public const int MaxObjectiveLength = 200;

    /// <summary>Slug de dataset: mesmo limite de <c>Datasets.Slug</c> e <c>Exercises.Dataset</c>.</summary>
    public const int MaxDatasetSlugLength = 100;

    /// <summary>
    /// Nível máximo de título (<c>#</c>, <c>##</c>, <c>###</c>) no <c>statement</c> do exercício.
    /// A página mostra o enunciado dentro da seção do exercício, com os títulos começando em &lt;h3&gt;
    /// (<c>LessonMarkdownRenderer.RenderFragment</c>) e teto em &lt;h6&gt;. Com até 3 níveis, o enunciado sai
    /// em h3..h5 e a hierarquia nunca se perde no teto.
    /// </summary>
    public const int MaxStatementHeadingLevel = 3;

    /// <summary>Devolve a lista de problemas; lista vazia significa conteúdo válido.</summary>
    /// <param name="paths">Trilhas lidas de content/paths.</param>
    /// <param name="datasets">
    /// Datasets de content/datasets. Quando informado, todo exercício que cita um dataset precisa ter o arquivo
    /// correspondente (o seed sempre informa). <c>null</c> pula essas regras.
    /// </param>
    public static IReadOnlyList<string> Validate(IReadOnlyList<PathContent> paths, IReadOnlyList<DatasetContent>? datasets = null)
    {
        var errors = new List<string>();
        var datasetSlugs = datasets is null ? null : ValidateDatasets(datasets, errors);

        if (paths.Count == 0)
        {
            errors.Add("Nenhuma trilha encontrada em content/paths.");
        }

        CheckSequentialOrder(paths.Select(p => p.Order), "trilhas", errors);

        foreach (var path in paths)
        {
            var where = $"content/paths/{path.Slug}/path.yaml";

            CheckSlug(path.Slug, $"content/paths/{path.Slug}: nome da pasta", errors);
            CheckLength(path.Title, MaxTitleLength, "title", where, errors);
            CheckLength(path.Description, MaxDescriptionLength, "description", where, errors);
            CheckLength(path.Icon, MaxIconLength, "icon", where, errors);

            if (path.EstimatedHours < 0)
            {
                errors.Add($"{where}: estimated_hours não pode ser negativo.");
            }

            if (path.Modules.Count == 0)
            {
                errors.Add($"{where}: nenhum módulo listado.");
            }

            CheckSequentialOrder(path.Modules.Select(m => m.Order), $"módulos de {where}", errors);
            CheckDuplicates(path.Modules.Select(m => m.Slug), $"{where}: módulo", errors);

            foreach (var module in path.Modules)
            {
                var moduleWhere = $"{where} (módulo '{module.Slug}')";

                CheckSlug(module.Slug, $"{moduleWhere}: slug", errors);
                CheckLength(module.Title, MaxTitleLength, "title", moduleWhere, errors);
                CheckLength(module.Description, MaxDescriptionLength, "description", moduleWhere, errors);

                if (module.Lessons.Count == 0)
                {
                    errors.Add($"{moduleWhere}: nenhuma aula em modules/{module.Slug}/.");
                }

                CheckSequentialOrder(module.Lessons.Select(l => l.Order), $"aulas de {moduleWhere}", errors);
                CheckDuplicates(module.Lessons.Select(l => l.Slug), $"{moduleWhere}: aula", errors);

                foreach (var lesson in module.Lessons)
                {
                    CheckSlug(lesson.Slug, $"{lesson.SourceFile}: slug do nome do arquivo", errors);
                    CheckLength(lesson.Title, MaxTitleLength, "title", lesson.SourceFile, errors);
                    CheckLength(lesson.Language, MaxLanguageLength, "language", lesson.SourceFile, errors);

                    if (lesson.ModuleSlug != module.Slug)
                    {
                        errors.Add($"{lesson.SourceFile}: frontmatter diz module '{lesson.ModuleSlug}', mas o arquivo está em modules/{module.Slug}/.");
                    }

                    if (lesson.EstimatedMinutes < 0)
                    {
                        errors.Add($"{lesson.SourceFile}: estimated_minutes não pode ser negativo.");
                    }

                    foreach (var (objective, index) in lesson.Objectives.Select((o, i) => (o, i + 1)))
                    {
                        if (string.IsNullOrWhiteSpace(objective))
                        {
                            errors.Add($"{lesson.SourceFile}: o item {index} de 'objectives' está vazio.");
                        }
                        else if (objective.Length > MaxObjectiveLength)
                        {
                            errors.Add($"{lesson.SourceFile}: o item {index} de 'objectives' tem {objective.Length} caracteres (máximo {MaxObjectiveLength}).");
                        }
                    }

                    if (lesson.Exercise is { } exercise)
                    {
                        CheckLength(exercise.Title, MaxTitleLength, "title", exercise.SourceFile, errors);
                        CheckLength(exercise.Language, MaxLanguageLength, "language", exercise.SourceFile, errors);
                        CheckStatementHeadings(exercise, errors);

                        if (exercise.Dataset is { } dataset && datasetSlugs is not null && !datasetSlugs.Contains(dataset))
                        {
                            errors.Add($"{exercise.SourceFile}: o dataset '{dataset}' não existe. "
                                + $"Crie content/datasets/{dataset}.sql ou corrija o campo 'dataset'.");
                        }
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>Lança <see cref="ContentValidationException"/> se houver qualquer problema.</summary>
    public static void EnsureValid(IReadOnlyList<PathContent> paths, IReadOnlyList<DatasetContent>? datasets = null)
    {
        var errors = Validate(paths, datasets);
        if (errors.Count > 0)
        {
            throw new ContentValidationException(errors);
        }
    }

    private static HashSet<string> ValidateDatasets(IReadOnlyList<DatasetContent> datasets, List<string> errors)
    {
        foreach (var dataset in datasets)
        {
            if (!IsValidSlug(dataset.Slug) || dataset.Slug.Length > MaxDatasetSlugLength)
            {
                errors.Add($"{dataset.SourceFile}: nome do arquivo '{dataset.Slug}' inválido para dataset: use só letras minúsculas "
                    + $"sem acento, números e hífens (máx. {MaxDatasetSlugLength}).");
            }

            if (string.IsNullOrWhiteSpace(dataset.Sql))
            {
                errors.Add($"{dataset.SourceFile}: o script do dataset está vazio.");
            }
        }

        CheckDuplicates(datasets.Select(d => d.Slug), "content/datasets: dataset", errors);
        return datasets.Select(d => d.Slug).ToHashSet(StringComparer.Ordinal);
    }

    // Lê o Markdown como o renderer lê (Markdig): título dentro de lista ou citação conta; "#### " em bloco de código, não.
    private static void CheckStatementHeadings(ExerciseContent exercise, List<string> errors)
    {
        if (string.IsNullOrEmpty(exercise.Statement))
        {
            return;
        }

        var document = Markdown.Parse(exercise.Statement);
        foreach (var heading in document.Descendants<HeadingBlock>().Where(h => h.Level > MaxStatementHeadingLevel))
        {
            errors.Add($"{exercise.SourceFile}: o 'statement' tem um título de nível {heading.Level} "
                + $"(linha {heading.Line + 1} do enunciado). Use no máximo {MaxStatementHeadingLevel} níveis (#, ## e ###): "
                + "o enunciado aparece a partir de <h3>, e níveis mais fundos se misturam no <h6>.");
        }
    }

    /// <summary>Slug no formato de URL: minúsculas, números e hífens (ex.: <c>sql-dados</c>).</summary>
    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxSlugLength && SlugPattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    private static void CheckSlug(string slug, string where, List<string> errors)
    {
        if (!IsValidSlug(slug))
        {
            errors.Add($"{where} '{slug}' inválido: use só letras minúsculas sem acento, números e hífens (máx. {MaxSlugLength}).");
        }
    }

    private static void CheckLength(string? value, int max, string field, string where, List<string> errors)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add($"{where}: '{field}' tem {value.Length} caracteres (máximo {max}).");
        }
    }

    private static void CheckDuplicates(IEnumerable<string> slugs, string what, List<string> errors)
    {
        foreach (var duplicated in slugs.GroupBy(s => s).Where(g => g.Count() > 1))
        {
            errors.Add($"{what} '{duplicated.Key}' aparece mais de uma vez.");
        }
    }

    // A ordem precisa ser 1, 2, 3... sem repetição e sem buracos.
    private static void CheckSequentialOrder(IEnumerable<int> orders, string what, List<string> errors)
    {
        var sorted = orders.Order().ToList();
        var expected = Enumerable.Range(1, sorted.Count).ToList();
        if (!sorted.SequenceEqual(expected))
        {
            errors.Add($"Ordem inválida em {what}: encontrado [{string.Join(", ", sorted)}], esperado [{string.Join(", ", expected)}].");
        }
    }
}
