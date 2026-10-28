using DevPath.Domain.CareerPaths;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class CareerPathTests
{
    [Fact]
    public void Create_ComTituloValido_GeraSlugAPartirDoTitulo()
    {
        var careerPath = CareerPath.Create("  Backend .NET  ");

        careerPath.Id.Should().NotBeEmpty();
        careerPath.Title.Should().Be("Backend .NET");
        careerPath.Slug.Should().Be("backend-net");
        careerPath.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("Programação Web", "programacao-web")]
    [InlineData("Lógica   de   Programação", "logica-de-programacao")]
    [InlineData("SQL & Dados", "sql-dados")]
    [InlineData("C# Avançado", "c-avancado")]
    public void Create_NormalizaSlugSemAcentosNemSimbolos(string title, string expectedSlug)
    {
        CareerPath.Create(title).Slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_SemTitulo_LancaArgumentException(string? title)
    {
        var act = () => CareerPath.Create(title!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("title");
    }

    [Fact]
    public void Create_TituloSoComSimbolos_LancaArgumentException()
    {
        // "!!!" não é vazio, mas geraria um slug vazio: a trilha ficaria sem URL.
        var act = () => CareerPath.Create("!!!");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("title");
    }

    [Fact]
    public void AddModule_DefineOrdemSequencialEVinculaNaTrilha()
    {
        var careerPath = CareerPath.Create("Backend .NET");

        var first = careerPath.AddModule("Fundamentos de C#");
        var second = careerPath.AddModule("ASP.NET Core MVC");

        careerPath.Modules.Should().HaveCount(2);
        first.Order.Should().Be(1);
        second.Order.Should().Be(2);
        second.CareerPathId.Should().Be(careerPath.Id);
        second.IsPublished.Should().BeFalse("módulos nascem como rascunho");
    }
}
