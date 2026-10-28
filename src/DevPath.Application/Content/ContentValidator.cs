using DevPath.Domain.CareerPaths;

namespace DevPath.Application.Content;

/// <summary>
/// Regras de consistência do conteúdo (SPEC §9.4), checadas antes de qualquer gravação no banco.
/// </summary>
/// <remarks>
/// O formato dos arquivos (YAML, frontmatter, campos obrigatórios) é checado por quem lê os arquivos.
/// Aqui ficam as regras entre itens: ordem, slugs e módulos sem aula.
/// </remarks>
public static class ContentValidator
{
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
            var where = $"trilha '{path.FolderName}'";

            // A trilha é encontrada no banco pelo slug, que o Domain deriva do título.
            var expectedSlug = TrySlug(path.Title);
            if (expectedSlug is null)
            {
                errors.Add($"{where}: título inválido '{path.Title}'.");
            }
            else if (expectedSlug != path.FolderName)
            {
                errors.Add($"{where}: a pasta deveria se chamar '{expectedSlug}' (slug do título '{path.Title}').");
            }

            if (path.EstimatedHours < 0)
            {
                errors.Add($"{where}: estimated_hours não pode ser negativo.");
            }

            if (path.Modules.Count == 0)
            {
                errors.Add($"{where}: nenhum módulo listado em path.yaml.");
            }

            CheckSequentialOrder(path.Modules.Select(m => m.Order), $"módulos de {where}", errors);

            foreach (var duplicated in path.Modules.GroupBy(m => m.Slug).Where(g => g.Count() > 1))
            {
                errors.Add($"{where}: módulo '{duplicated.Key}' listado mais de uma vez.");
            }

            foreach (var module in path.Modules)
            {
                var moduleWhere = $"{where}, módulo '{module.Slug}'";

                if (module.Lessons.Count == 0)
                {
                    errors.Add($"{moduleWhere}: nenhuma aula em modules/{module.Slug}/.");
                }

                CheckSequentialOrder(module.Lessons.Select(l => l.Order), $"aulas de {moduleWhere}", errors);

                foreach (var lesson in module.Lessons)
                {
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

    private static string? TrySlug(string title)
    {
        try
        {
            return CareerPath.Create(title).Slug;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
