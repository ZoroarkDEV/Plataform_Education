using DevPath.Application.Content;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
public sealed partial class FileSystemContentSource(string contentRoot) : IContentSource
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

        var file = Deserialize<PathFile>(File.ReadAllText(yamlFile), yamlFile, firstLine: 1, errors);
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

            // Exercício sem a aula correspondente provavelmente é arquivo renomeado pela metade.
            foreach (var exerciseFile in Directory.GetFiles(moduleDir, "*" + ExerciseSuffix)
                         .Where(f => !File.Exists(f[..^ExerciseSuffix.Length] + ".md")))
            {
                errors.Add($"{Relative(exerciseFile)}: exercício sem aula. O arquivo da aula deveria ser {Path.GetFileName(exerciseFile)[..^ExerciseSuffix.Length]}.md.");
            }

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
            Slug: Path.GetFileName(pathDir),
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

        // O frontmatter começa na linha 2 do arquivo (a linha 1 é o '---').
        var file = Deserialize<LessonFile>(frontmatter, lessonFile, firstLine: 2, errors);
        if (file is null)
        {
            return null;
        }

        // 02-joins.md -> ordem 2, slug "joins". O slug identifica a aula no banco (progresso fica preso a ele).
        var name = LessonFileName().Match(Path.GetFileName(lessonFile));
        if (!name.Success)
        {
            errors.Add($"{where}: o nome do arquivo deve seguir o padrão NN-slug.md (ex.: 02-joins.md).");
            return null;
        }

        var order = RequiredNumber(file.Order, "order", where, errors);
        if (file.Order is not null && int.Parse(name.Groups["order"].Value) != order)
        {
            errors.Add($"{where}: o prefixo do arquivo ({name.Groups["order"].Value}) não bate com order: {order}.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            errors.Add($"{where}: a aula não tem texto depois do frontmatter.");
        }

        return new LessonContent(
            SourceFile: where,
            Slug: name.Groups["slug"].Value,
            Title: Required(file.Title, "title", where, errors),
            ModuleSlug: Required(file.Module, "module", where, errors),
            Order: order,
            EstimatedMinutes: RequiredNumber(file.EstimatedMinutes, "estimated_minutes", where, errors),
            Language: string.IsNullOrWhiteSpace(file.Language) ? null : file.Language.Trim().ToLowerInvariant(),
            Difficulty: ParseDifficulty(file.Difficulty, "difficulty", where, errors),
            Body: body.Trim() + Environment.NewLine)
        {
            Objectives = (file.Objectives ?? []).Select(o => (o ?? string.Empty).Trim()).ToList(),
            IsDraft = file.Draft ?? false,
            Exercise = ReadExercise(lessonFile[..^".md".Length] + ExerciseSuffix, errors)
        };
    }

    // ---- Exercícios: NN-slug.exercise.json ao lado da aula ----

    public const string ExerciseSuffix = ".exercise.json";

    private static readonly JsonSerializerOptions ExerciseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private ExerciseContent? ReadExercise(string exerciseFile, List<string> errors)
    {
        if (!File.Exists(exerciseFile))
        {
            return null;
        }

        var where = Relative(exerciseFile);
        ExerciseFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExerciseFile>(File.ReadAllText(exerciseFile), ExerciseJson);
        }
        catch (JsonException ex)
        {
            var line = (ex.LineNumber ?? 0) + 1;
            var unknown = UnmappedJsonMessage().Match(ex.Message);
            errors.Add(unknown.Success
                ? $"{where}, linha {line}: chave desconhecida '{unknown.Groups["key"].Value}'."
                : $"{where}, linha {line}: JSON inválido ou valor com tipo errado em '{ex.Path}'.");
            return null;
        }

        if (file is null)
        {
            errors.Add($"{where}: arquivo vazio.");
            return null;
        }

        var testCases = new List<ExerciseTestCase>();
        foreach (var (test, index) in (file.TestCases ?? []).Select((t, i) => (t, i)))
        {
            var testWhere = $"{where} (test_cases[{index}])";
            var columns = (test.Columns ?? []).Select(c => (c ?? string.Empty).Trim()).ToList();
            if (columns.Count == 0 || columns.Any(c => c.Length == 0))
            {
                errors.Add($"{testWhere}: 'columns' precisa listar o nome de cada coluna esperada.");
            }

            var rows = new List<IReadOnlyList<object?>>();
            foreach (var (row, rowIndex) in (test.Rows ?? []).Select((r, i) => (r, i)))
            {
                if (row.Count != columns.Count)
                {
                    errors.Add($"{testWhere}: a linha {rowIndex + 1} de 'rows' tem {row.Count} valores, mas há {columns.Count} colunas.");
                }

                rows.Add(row.Select(v => ToValue(v, testWhere, errors)).ToList());
            }

            testCases.Add(new ExerciseTestCase(
                Required(test.Name, "name", testWhere, errors), columns, rows,
                test.OrderMatters ?? false, test.CheckColumnNames ?? false));
        }

        if (testCases.Count == 0)
        {
            errors.Add($"{where}: 'test_cases' precisa ter pelo menos um caso.");
        }

        var dataset = string.IsNullOrWhiteSpace(file.Dataset) ? null : file.Dataset.Trim();
        if (dataset is not null && !File.Exists(DatasetPath(dataset)))
        {
            errors.Add($"{where}: dataset '{dataset}' não encontrado em content/datasets/{dataset}.sql.");
        }

        var points = file.Points ?? 10;
        if (points <= 0)
        {
            errors.Add($"{where}: 'points' precisa ser maior que zero.");
        }

        var commonErrors = (file.CommonErrors ?? [])
            .Select((e, i) => new ExerciseCommonError(
                Required(e.Example, "example", $"{where} (common_errors[{i}])", errors),
                Required(e.Message, "message", $"{where} (common_errors[{i}])", errors)))
            .ToList();

        return new ExerciseContent(
            SourceFile: where,
            Title: Required(file.Title, "title", where, errors),
            Statement: Required(file.Statement, "statement", where, errors),
            Language: Required(file.Language, "language", where, errors).ToLowerInvariant(),
            Dataset: dataset,
            Points: points,
            StarterCode: file.StarterCode ?? string.Empty,
            Solution: Required(file.Solution, "solution", where, errors),
            Hints: (file.Hints ?? []).Select(h => (h ?? string.Empty).Trim()).Where(h => h.Length > 0).ToList(),
            TestCases: testCases,
            CommonErrors: commonErrors,
            TestCasesJson: JsonSerializer.Serialize(file.TestCases ?? [], ExerciseJson));
    }

    /// <summary>Caminho do script de um dataset de exercícios (<c>content/datasets/&lt;nome&gt;.sql</c>).</summary>
    public string DatasetPath(string dataset) => Path.Combine(ContentRoot, "datasets", dataset + ".sql");

    private static object? ToValue(JsonElement value, string where, List<string> errors)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return value.GetString();
            case JsonValueKind.Number: return value.GetDecimal();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            default:
                errors.Add($"{where}: valor '{value}' em 'rows' precisa ser texto, número, true/false ou null.");
                return null;
        }
    }

    [GeneratedRegex(@"property '(?<key>[^']+)' could not be mapped")]
    private static partial Regex UnmappedJsonMessage();

    private sealed class ExerciseFile
    {
        public string? Title { get; set; }
        public string? Statement { get; set; }
        public string? Language { get; set; }
        public string? Dataset { get; set; }
        public int? Points { get; set; }
        public string? StarterCode { get; set; }
        public string? Solution { get; set; }
        public List<string?>? Hints { get; set; }
        public List<TestCaseEntry>? TestCases { get; set; }
        public List<CommonErrorEntry>? CommonErrors { get; set; }
    }

    private sealed class TestCaseEntry
    {
        public string? Name { get; set; }
        public List<string?>? Columns { get; set; }
        public List<List<JsonElement>>? Rows { get; set; }
        public bool? OrderMatters { get; set; }
        public bool? CheckColumnNames { get; set; }
    }

    private sealed class CommonErrorEntry
    {
        public string? Example { get; set; }
        public string? Message { get; set; }
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

    // firstLine: em que linha do arquivo o YAML começa (path.yaml = 1; frontmatter = 2).
    private T? Deserialize<T>(string yaml, string file, int firstLine, List<string> errors) where T : class
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
            errors.Add($"{Relative(file)}, linha {ex.Start.Line + firstLine - 1}: {DescribeYamlError<T>(ex, yaml)}");
            return null;
        }
    }

    // Traduz o erro do YamlDotNet para algo útil a quem edita conteúdo, sem nomes de tipos .NET.
    private static string DescribeYamlError<T>(YamlException ex, string yaml)
    {
        var unknownKey = UnknownPropertyMessage().Match(ex.Message);
        if (unknownKey.Success)
        {
            return $"chave desconhecida '{unknownKey.Groups["key"].Value}'. Chaves aceitas: {string.Join(", ", AcceptedKeys(ex, typeof(T)))}.";
        }

        // Erro de valor (ex.: "order: dois"): descobre a chave pela própria linha do YAML.
        var lines = yaml.ReplaceLineEndings("\n").Split('\n');
        var lineIndex = (int)ex.Start.Line - 1;
        var key = lineIndex >= 0 && lineIndex < lines.Length ? KeyOfLine().Match(lines[lineIndex]) : Match.Empty;

        if (ex is not SyntaxErrorException && key.Success)
        {
            var field = key.Groups["key"].Value;
            return NumericKeys.Contains(field)
                ? $"valor inválido em '{field}': esperado um número inteiro."
                : $"valor inválido em '{field}'.";
        }

        return "YAML mal formatado (confira indentação, ':' depois de cada chave e aspas/colchetes fechados).";
    }

    private static readonly HashSet<string> NumericKeys = ["order", "estimated_minutes", "estimated_hours"];

    // Lista as chaves do bloco onde a chave desconhecida apareceu (raiz do arquivo ou item de modules).
    private static IEnumerable<string> AcceptedKeys(YamlException ex, Type rootType)
    {
        var type = rootType == typeof(PathFile) && ex.Start.Column > 1 ? typeof(ModuleEntry) : rootType;
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => UnderscoredNamingConvention.Instance.Apply(p.Name));
    }

    [GeneratedRegex(@"Property '(?<key>[^']+)' not found")]
    private static partial Regex UnknownPropertyMessage();

    [GeneratedRegex(@"^\s*(-\s*)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*:")]
    private static partial Regex KeyOfLine();

    [GeneratedRegex(@"^(?<order>\d+)-(?<slug>.+)\.md$")]
    private static partial Regex LessonFileName();

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
        public List<string?>? Objectives { get; set; }
        public bool? Draft { get; set; }
    }
}
