using DevPath.Application.Content;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Application;

public class ContentValidatorTests
{
    [Fact]
    public void Validate_ConteudoConsistente_NaoRetornaErros()
    {
        var paths = new[] { Path("sql-dados", "SQL & Dados", 1, Module("ambiente", 1, Lesson("ambiente", 1), Lesson("ambiente", 2))) };

        ContentValidator.Validate(paths).Should().BeEmpty();
    }

    [Fact]
    public void Validate_PastaDiferenteDoSlugDoTitulo_EhPermitido()
    {
        // O slug da trilha é o nome da pasta e não depende do título: renomear o título não cria outra trilha.
        var paths = new[] { Path("sql", "SQL & Dados (novo nome)", 1, Module("ambiente", 1, Lesson("ambiente", 1))) };

        ContentValidator.Validate(paths).Should().BeEmpty();
    }

    [Theory]
    [InlineData("SQL-Dados")]
    [InlineData("sql_dados")]
    [InlineData("sql--dados")]
    [InlineData("-sql")]
    [InlineData("introdução")]
    public void Validate_SlugForaDoFormato_RetornaErro(string slug)
    {
        var paths = new[] { Path(slug, "SQL", 1, Module("ambiente", 1, Lesson("ambiente", 1))) };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.Contains($"'{slug}' inválido"));
    }

    [Fact]
    public void Validate_SlugDeAulaRepetidoNoModulo_RetornaErro()
    {
        var paths = new[]
        {
            Path("sql-dados", "SQL", 1, Module("ambiente", 1, Lesson("ambiente", 1, "joins"), Lesson("ambiente", 2, "joins")))
        };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.Contains("aula 'joins' aparece mais de uma vez"));
    }

    [Fact]
    public void Validate_TituloMaiorQue200Caracteres_RetornaErroComArquivo()
    {
        var lesson = Lesson("ambiente", 1) with { Title = new string('a', 201) };
        var paths = new[] { Path("sql-dados", "SQL", 1, Module("ambiente", 1, lesson)) };

        ContentValidator.Validate(paths).Should().ContainSingle(e =>
            e.StartsWith(lesson.SourceFile) && e.Contains("'title' tem 201 caracteres (máximo 200)"));
    }

    [Fact]
    public void Validate_TamanhosNoLimite_NaoRetornaErros()
    {
        var lesson = Lesson("ambiente", 1) with { Title = new string('a', 200), Language = new string('b', 50) };
        var path = Path("sql-dados", new string('t', 200), 1, Module("ambiente", 1, lesson)) with
        {
            Description = new string('d', 2000),
            Icon = new string('i', 100)
        };

        ContentValidator.Validate([path]).Should().BeEmpty();
    }

    [Fact]
    public void Validate_DescricaoEIconeAcimaDoLimite_RetornaErros()
    {
        var path = Path("sql-dados", "SQL", 1, Module("ambiente", 1, Lesson("ambiente", 1))) with
        {
            Description = new string('d', 2001),
            Icon = new string('i', 101)
        };

        ContentValidator.Validate([path]).Should().HaveCount(2)
            .And.Contain(e => e.Contains("'description' tem 2001"))
            .And.Contain(e => e.Contains("'icon' tem 101"));
    }

    [Theory]
    [InlineData(1, 1)] // repetida
    [InlineData(1, 3)] // buraco
    [InlineData(0, 1)] // começa em 0
    public void Validate_OrdemDosModulosInconsistente_RetornaErro(int first, int second)
    {
        var paths = new[]
        {
            Path("sql-dados", "SQL & Dados", 1,
                Module("ambiente", first, Lesson("ambiente", 1)),
                Module("crud", second, Lesson("crud", 1)))
        };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.StartsWith("Ordem inválida em módulos"));
    }

    [Fact]
    public void Validate_OrdemDasAulasComBuraco_RetornaErro()
    {
        var paths = new[] { Path("sql-dados", "SQL & Dados", 1, Module("ambiente", 1, Lesson("ambiente", 1), Lesson("ambiente", 3))) };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.StartsWith("Ordem inválida em aulas"));
    }

    [Fact]
    public void Validate_ModuloSemAulas_RetornaErro()
    {
        var paths = new[] { Path("sql-dados", "SQL & Dados", 1, Module("ambiente", 1)) };

        ContentValidator.Validate(paths).Should().Contain(e => e.Contains("nenhuma aula"));
    }

    [Fact]
    public void Validate_AulaApontandoParaOutroModulo_RetornaErro()
    {
        var paths = new[] { Path("sql-dados", "SQL & Dados", 1, Module("ambiente", 1, Lesson("crud", 1))) };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.Contains("frontmatter diz module 'crud'"));
    }

    [Fact]
    public void EnsureValid_ComErros_LancaExcecaoListandoTodos()
    {
        var paths = new[] { Path("errado", "SQL & Dados", 2, Module("ambiente", 1)) };

        var act = () => ContentValidator.EnsureValid(paths);

        act.Should().Throw<ContentValidationException>()
            .Which.Errors.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Validate_ObjetivosValidos_NaoRetornaErros()
    {
        var lesson = Lesson("ambiente", 1) with { Objectives = ["Escrever um SELECT", new string('a', ContentValidator.MaxObjectiveLength)] };

        ContentValidator.Validate([Path("sql-dados", "SQL", 1, Module("ambiente", 1, lesson))]).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ObjetivoVazio_RetornaErroComPosicao()
    {
        var lesson = Lesson("ambiente", 1) with { Objectives = ["Escrever um SELECT", "  "] };

        ContentValidator.Validate([Path("sql-dados", "SQL", 1, Module("ambiente", 1, lesson))])
            .Should().ContainSingle(e => e.Contains("o item 2 de 'objectives' está vazio"));
    }

    [Fact]
    public void Validate_ObjetivoComMaisDe200Caracteres_RetornaErro()
    {
        var lesson = Lesson("ambiente", 1) with { Objectives = [new string('a', ContentValidator.MaxObjectiveLength + 1)] };

        ContentValidator.Validate([Path("sql-dados", "SQL", 1, Module("ambiente", 1, lesson))])
            .Should().ContainSingle(e => e.Contains("o item 1 de 'objectives' tem 201 caracteres (máximo 200)"));
    }

    private static PathContent Path(string folder, string title, int order, params ModuleContent[] modules) =>
        new(folder, title, "Descrição", "icon", DifficultyLevel.Beginner, 10, order, modules);

    private static ModuleContent Module(string slug, int order, params LessonContent[] lessons) =>
        new(slug, $"Módulo {slug}", "Descrição", order, lessons);

    private static LessonContent Lesson(string moduleSlug, int order, string? slug = null) =>
        new($"content/{moduleSlug}/{order:00}-aula-{order}.md", slug ?? $"aula-{order}", $"Aula {order}", moduleSlug, order, 10, "sql", DifficultyLevel.Beginner, "# Aula\n");

    [Fact]
    public void MaxObjectiveLength_EhOMesmoLimiteDoDominio()
    {
        // O validador barra no /content o que o Domain (Lesson.UpdateContent) recusaria no seed.
        ContentValidator.MaxObjectiveLength.Should().Be(DevPath.Domain.CareerPaths.Lesson.MaxObjectiveLength);
    }
}
