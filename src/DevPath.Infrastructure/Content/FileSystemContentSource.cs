using DevPath.Application.Content;
using DevPath.Domain.Common;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DevPath.Infrastructure.Content;

/// <summary>
/// Lê as trilhas de <c>content/paths/&lt;slug&gt;/</c>: o <c>path.yaml</c> e as aulas em Markdown
/// com frontmatter YAML (formato documentado em <c>content/README.md</c>).
/// </summary>
/// <remarks>
/// Junta todos os problemas encontrados (arquivo faltando, YAML inválido, campo obrigatório vazio)
/// e lança uma única <see cref="ContentValidationException"/> no fim, para quem edita o conteúdo
/// ver tudo de uma vez.
/// </remarks>
public sealed class FileSystemContentSource(string contentRoot) : IContentSource
{
    // snake_case nos arquivos (estimated_minutes) -> PascalCase nas classes (EstimatedMinutes).
    // Chave desconhecida gera erro: assim um erro de digitação não passa em silêncio.
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public string ContentRoot { get; } = Path.GetFullPath(contentRoot);

    public IReadOnlyList<PathContent> LoadPaths()
    {
        var errors = new List<string>();
        var paths = new List<PathContent>();

        var pathsDir = Path.Combine(ContentRoot, "paths");
        if (!Directory.Exists(pathsDir))
        {
            throw new ContentValidationException([$"Pasta não encontrada: {pathsDir}"]);
        }

        foreach (var pathDir in Directory.GetDirectories(pathsDir).Order(StringComparer.Ordinal))
        {
            var path = ReadPath(pathDir, errors);
            if (path is not null)
            {
                paths.Add(path);
            }
        }

        if (errors.Count > 0)
        {
            throw new ContentValidationException(errors);
        }

        return paths;
    }

    private PathContent? ReadPath(string pathDir, List<string> errors)
    {
        var yamlFile = Path.Combine(pathDir, "path.yaml");
        if (!File.Exists(yamlFile))
        {
            errors.Add($"{Relative(pathDir)}: falta o arquivo path.yaml.");
            return null;
        }

        var file = Deserialize<PathFile>(File.ReadAllText(yamlFile), yamlFile, errors);
        if (file is null)
        {
            return null;
        }

        var where = Relative(yamlFile);
        var title = Required(file.Title, "title", where, errors);
        var description = Required(file.Description, "description", where, errors);
        var difficulty = ParseDifficulty(file.DifficultyLevel, "difficulty_level", where, errors);
        var estimatedHours = RequiredNumber(file.EstimatedHours, "estimated_hours", where, errors);
        var order = RequiredNumber(file.Order, "order", where, errors);

        var modulesDir = Path.Combine(pathDir, "modules");
        var listedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var modules = new List<ModuleContent>();

        foreach (var (entry, index) in (file.Modules ?? []).Select((m, i) => (m, i)))
        {
            var moduleWhere = $"{where} (modules[{index}])";
            var slug = Required(entry.Slug, "slug", moduleWhere, errors);
            var moduleTitle = Required(entry.Title, "title", moduleWhere, errors);
            var moduleOrder = RequiredNumber(entry.Order, "order", moduleWhere, errors);
            if (slug.Length == 0)
            {
                continue;
            }

            listedSlugs.Add(slug);
            var moduleDir = Path.Combine(modulesDir, slug);
            if (!Directory.Exists(moduleDir))
            {
                errors.Add($"{moduleWhere}: módulo '{slug}' listado, mas a pasta {Relative(moduleDir)} não existe.");
                continue;
            }

            var lessons = Directory.GetFiles(moduleDir, "*.md")
                .Order(StringComparer.Ordinal)
                .Select(lessonFile => ReadLesson(lessonFile, errors))
                .OfType<LessonContent>()
                .ToList();

            modules.Add(new ModuleContent(slug, moduleTitle, (entry.Description ?? string.Empty).Trim(), moduleOrder, lessons));
        }

        // Pasta de módulo que ninguém listou no path.yaml provavelmente é esquecimento.
        if (Directory.Exists(modulesDir))
        {
            foreach (var orphan in Directory.GetDirectories(modulesDir).Where(d => !listedSlugs.Contains(Path.GetFileName(d))))
            {
                errors.Add($"{Relative(orphan)}: pasta de módulo não listada em {where}.");
            }
        }

        return new PathContent(
            FolderName: Path.GetFileName(pathDir),
            Title: title,
            Description: description,
            Icon: string.IsNullOrWhiteSpace(file.Icon) ? null : file.Icon.Trim(),
            DifficultyLevel: difficulty,
            EstimatedHours: estimatedHours,
            Order: order,
            Modules: modules);
    }

    private LessonContent? ReadLesson(string lessonFile, List<string> errors)
    {
        var where = Relative(lessonFile);
        var (frontmatter, body) = SplitFrontmatter(File.ReadAllText(lessonFile));
        if (frontmatter is null)
        {
            errors.Add($"{where}: a aula precisa começar com um frontmatter YAML entre linhas '---'.");
            return null;
        }

        var file = Deserialize<LessonFile>(frontmatter, lessonFile, errors);
        if (file is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            errors.Add($"{where}: a aula não tem texto depois do frontmatter.");
        }

        return new LessonContent(
            SourceFile: where,
            Title: Required(file.Title, "title", where, errors),
            ModuleSlug: Required(file.Module, "module", where, errors),
            Order: RequiredNumber(file.Order, "order", where, errors),
            EstimatedMinutes: RequiredNumber(file.EstimatedMinutes, "estimated_minutes", where, errors),
            Language: string.IsNullOrWhiteSpace(file.Language) ? null : file.Language.Trim().ToLowerInvariant(),
            Difficulty: ParseDifficulty(file.Difficulty, "difficulty", where, errors),
            Body: body.Trim() + Environment.NewLine);
    }

    // "---\n<yaml>\n---\n<markdown>" -> (yaml, markdown). Sem frontmatter -> (null, texto inteiro).
    public static (string? Frontmatter, string Body) SplitFrontmatter(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (null, text);
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
        {
            return (null, text);
        }

        var frontmatter = string.Join('\n', lines[1..end]);
        var body = string.Join('\n', lines[(end + 1)..]);
        return (frontmatter, body);
    }

    private T? Deserialize<T>(string yaml, string file, List<string> errors) where T : class
    {
        try
        {
            var result = Yaml.Deserialize<T>(yaml);
            if (result is null)
            {
                errors.Add($"{Relative(file)}: arquivo YAML vazio.");
            }

            return result;
        }
        catch (YamlException ex)
        {
            errors.Add($"{Relative(file)}: YAML inválido ({ex.Message} {ex.InnerException?.Message})".Trim());
            return null;
        }
    }

    private static string Required(string? value, string field, string where, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{where}: campo obrigatório '{field}' ausente ou vazio.");
            return string.Empty;
        }

        return value.Trim();
    }

    private static int RequiredNumber(int? value, string field, string where, List<string> errors)
    {
        if (value is null)
        {
            errors.Add($"{where}: campo obrigatório '{field}' ausente.");
            return 0;
        }

        return value.Value;
    }

    // Aceita só os nomes (beginner, intermediate, advanced), nunca números.
    private static DifficultyLevel ParseDifficulty(string? value, string field, string where, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && char.IsLetter(value.Trim()[0])
            && Enum.TryParse<DifficultyLevel>(value.Trim(), ignoreCase: true, out var level))
        {
            return level;
        }

        var valid = string.Join(", ", Enum.GetNames<DifficultyLevel>().Select(n => n.ToLowerInvariant()));
        errors.Add($"{where}: '{field}' inválido ('{value}'). Use: {valid}.");
        return DifficultyLevel.Beginner;
    }

    private string Relative(string fullPath) =>
        Path.Combine("content", Path.GetRelativePath(ContentRoot, fullPath)).Replace('\\', '/');

    // Formato dos arquivos. Campos numéricos são int? para detectar "campo ausente".
    private sealed class PathFile
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? DifficultyLevel { get; set; }
        public int? EstimatedHours { get; set; }
        public int? Order { get; set; }
        public List<ModuleEntry>? Modules { get; set; }
    }

    private sealed class ModuleEntry
    {
        public string? Slug { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int? Order { get; set; }
    }

    private sealed class LessonFile
    {
        public string? Title { get; set; }
        public string? Module { get; set; }
        public int? Order { get; set; }
        public int? EstimatedMinutes { get; set; }
        public string? Language { get; set; }
        public string? Difficulty { get; set; }
    }
}
