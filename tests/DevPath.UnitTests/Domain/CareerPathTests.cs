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
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_SemTitulo_LancaArgumentException(string title)
    {
        var act = () => CareerPath.Create(title);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("title");
    }
}
