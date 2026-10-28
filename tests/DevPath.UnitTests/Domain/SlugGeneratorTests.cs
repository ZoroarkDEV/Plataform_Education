using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Programação Web", "programacao-web")]
    [InlineData("  Introdução   ao   SQL  ", "introducao-ao-sql")]
    [InlineData("Ações & Funções", "acoes-funcoes")]
    public void Generate_RemoveAcentosESimbolos(string value, string expected)
    {
        SlugGenerator.Generate(value).Should().Be(expected);
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
