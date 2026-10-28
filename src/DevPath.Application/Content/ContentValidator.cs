using System.Text.RegularExpressions;

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

    /// <summary>Devolve a lista de problemas; lista vazia significa conteúdo válido.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<PathContent> paths)
    {
        var errors = new List<string>();

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
                }
            }
        }

        return errors;
    }

    /// <summary>Lança <see cref="ContentValidationException"/> se houver qualquer problema.</summary>
    public static void EnsureValid(IReadOnlyList<PathContent> paths)
    {
        var errors = Validate(paths);
        if (errors.Count > 0)
        {
            throw new ContentValidationException(errors);
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
