using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Programação Web", "programacao-web")]
    [InlineData("  Introdução   ao   SQL  ", "introducao-ao-sql")]
    [InlineData("Ações & Funções", "acoes-funcoes")]
    // Casos da revisão de QA (#8 / #23): todo não alfanumérico separa palavras.
    [InlineData("Front-end", "front-end")]
    [InlineData("C# e .NET", "c-e-net")]
    [InlineData("Node.js/Express", "node-js-express")]
    [InlineData("ASP.NET Core", "asp-net-core")]
    [InlineData("snake_case_e--hifens", "snake-case-e-hifens")]
    [InlineData("  -- Título --  ", "titulo")]
    public void Generate_RemoveAcentosESimbolos(string value, string expected)
    {
        SlugGenerator.Generate(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("performance-e-confiabilidade")]
    [InlineData("Programação Web: do Zero ao Deploy!")]
    [InlineData("C# e .NET")]
    public void Generate_EhIdempotente(string value)
    {
        var once = SlugGenerator.Generate(value);

        SlugGenerator.Generate(once).Should().Be(once);
    }

    [Theory]
    [InlineData("a-b-c", "a-b-c")]
    [InlineData("pgadmin-e-dbeaver", "pgadmin-e-dbeaver")]
    public void ChangeSlug_ComSlugJaHifenizado_MantemOsHifens(string slug, string expected)
    {
        var module = Module.Create(Guid.NewGuid(), "Ferramentas", order: 1);

        module.ChangeSlug(slug);

        module.Slug.Should().Be(expected);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("_/_")]
    public void Generate_SoSeparadores_LancaArgumentException(string value)
    {
        var act = () => SlugGenerator.Generate(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_SemLetrasNemNumeros_LancaComONomeDoArgumento()
    {
        var title = "!!!";

        var act = () => SlugGenerator.Generate(title);

        // [CallerArgumentExpression] preenche o paramName com "title" automaticamente.
        act.Should().Throw<ArgumentException>().WithParameterName("title");
    }
}
