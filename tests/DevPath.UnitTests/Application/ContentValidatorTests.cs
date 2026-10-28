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
    public void Validate_PastaDiferenteDoSlugDoTitulo_RetornaErro()
    {
        var paths = new[] { Path("sql", "SQL & Dados", 1, Module("ambiente", 1, Lesson("ambiente", 1))) };

        ContentValidator.Validate(paths).Should().ContainSingle(e => e.Contains("deveria se chamar 'sql-dados'"));
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

    private static PathContent Path(string folder, string title, int order, params ModuleContent[] modules) =>
        new(folder, title, "Descrição", "icon", DifficultyLevel.Beginner, 10, order, modules);

    private static ModuleContent Module(string slug, int order, params LessonContent[] lessons) =>
        new(slug, $"Módulo {slug}", "Descrição", order, lessons);

    private static LessonContent Lesson(string moduleSlug, int order) =>
        new($"content/{moduleSlug}/{order:00}.md", $"Aula {order}", moduleSlug, order, 10, "sql", DifficultyLevel.Beginner, "# Aula\n");
}
