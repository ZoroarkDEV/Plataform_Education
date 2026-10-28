using DevPath.Application.Content;
using DevPath.Domain.Common;
using DevPath.Infrastructure.Content;
using FluentAssertions;

namespace DevPath.ContentTests;

/// <summary>Leitura de arquivos em uma pasta temporária, para cobrir os casos de erro.</summary>
public sealed class FileSystemContentSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("devpath-content-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LoadPaths_ArquivosValidos_LeTrilhaModuloEAula()
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", Frontmatter() + "\n# Intro\n\nTexto da aula.\n");

        var path = new FileSystemContentSource(_root).LoadPaths().Single();

        path.Title.Should().Be("SQL & Dados");
        path.DifficultyLevel.Should().Be(DifficultyLevel.Beginner);
        var lesson = path.Modules.Single().Lessons.Single();
        lesson.Title.Should().Be("Introdução");
        lesson.Language.Should().Be("sql");
        lesson.EstimatedMinutes.Should().Be(15);
        lesson.Body.Should().StartWith("# Intro").And.NotContain("---");
    }

    [Fact]
    public void LoadPaths_SemPathYaml_LancaErro()
    {
        Directory.CreateDirectory(Path.Combine(_root, "paths", "sql-dados"));

        Errors().Should().ContainSingle(e => e.Contains("falta o arquivo path.yaml"));
    }

    [Fact]
    public void LoadPaths_ModuloListadoSemPasta_LancaErro()
    {
        WritePath();

        Errors().Should().ContainSingle(e => e.Contains("módulo 'ambiente' listado, mas a pasta"));
    }

    [Fact]
    public void LoadPaths_PastaDeModuloNaoListada_LancaErro()
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", Frontmatter() + "\n# Intro\n");
        WriteLesson("esquecido", "01-x.md", Frontmatter() + "\n# X\n");

        Errors().Should().ContainSingle(e => e.Contains("pasta de módulo não listada"));
    }

    [Fact]
    public void LoadPaths_AulaSemFrontmatter_LancaErro()
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", "# Intro sem frontmatter\n");

        Errors().Should().ContainSingle(e => e.Contains("frontmatter YAML"));
    }

    [Fact]
    public void LoadPaths_FrontmatterSemCampoObrigatorio_LancaErro()
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", Frontmatter().Replace("estimated_minutes: 15\n", "") + "\n# Intro\n");

        Errors().Should().ContainSingle(e => e.Contains("'estimated_minutes' ausente"));
    }

    [Fact]
    public void LoadPaths_ChaveDesconhecida_LancaErroDeYaml()
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", Frontmatter().Replace("order: 1", "ordem: 1") + "\n# Intro\n");

        Errors().Should().ContainSingle(e => e.Contains("YAML inválido"));
    }

    [Theory]
    [InlineData("facil")]
    [InlineData("1")]
    public void LoadPaths_DificuldadeInvalida_LancaErro(string difficulty)
    {
        WritePath();
        WriteLesson("ambiente", "01-intro.md", Frontmatter().Replace("difficulty: beginner", $"difficulty: \"{difficulty}\"") + "\n# Intro\n");

        Errors().Should().ContainSingle(e => e.Contains("'difficulty' inválido"));
    }

    [Theory]
    [InlineData("---\ntitle: x\n---\ncorpo", "title: x", "corpo")]
    [InlineData("---\r\ntitle: x\r\n---\r\ncorpo", "title: x", "corpo")]
    public void SplitFrontmatter_SeparaYamlDoCorpo(string text, string yaml, string body)
    {
        var (frontmatter, rest) = FileSystemContentSource.SplitFrontmatter(text);

        frontmatter.Should().Be(yaml);
        rest.Should().Be(body);
    }

    private IReadOnlyList<string> Errors()
    {
        var act = () => new FileSystemContentSource(_root).LoadPaths();
        return act.Should().Throw<ContentValidationException>().Which.Errors;
    }

    private void WritePath() => Write(Path.Combine("paths", "sql-dados", "path.yaml"), """
        title: "SQL & Dados"
        description: "Trilha de teste"
        icon: database
        difficulty_level: beginner
        estimated_hours: 10
        order: 1
        modules:
          - slug: ambiente
            title: "Ambiente"
            description: "Docker e PostgreSQL"
            order: 1
        """);

    private void WriteLesson(string module, string file, string text) =>
        Write(Path.Combine("paths", "sql-dados", "modules", module, file), text);

    private static string Frontmatter() =>
        "---\ntitle: \"Introdução\"\nmodule: ambiente\norder: 1\nestimated_minutes: 15\nlanguage: SQL\ndifficulty: beginner\n---\n";

    private void Write(string relativePath, string text)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
