using DevPath.Application.Lessons;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Application;

public class LessonContractsTests
{
    [Fact]
    public void ExerciseDto_NaoTemCampoDeSolucaoCasosDeTesteOuErrosComuns()
    {
        // ADR-003: solução, linhas esperadas e common_errors ficam só no servidor.
        var names = typeof(ExerciseDto).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();

        names.Should().BeEquivalentTo(["title", "statementmarkdown", "language", "dataset", "startercode", "hints", "points", "id", "datasetdisplayname"],
            "Id e DatasetDisplayName (ADR-007) são o que a tela usa; não revelam solução");
        names.Should().NotContain(n => n.Contains("solution") || n.Contains("test") || n.Contains("error") || n.Contains("expected"));
    }

    [Fact]
    public void ExerciseDto_PontosPadraoSao10()
    {
        new ExerciseDto("Ex", "Enunciado", "sql", null, null, []).Points.Should().Be(10);
    }

    [Fact]
    public void LessonDto_SemExercicio_ContinuaCompilandoComOsParametrosAntigos()
    {
        var dto = new LessonDto(Guid.NewGuid(), "a", "A", 5, DifficultyLevel.Beginner, null, [], "# a",
            new LessonBreadcrumb("p", "P", "m", "M"), null, null);

        dto.Exercise.Should().BeNull();
        dto.Position.Should().Be(0);
    }
}

public class DatasetDisplayNamesTests
{
    [Theory]
    [InlineData("livraria", "Livraria")]
    [InlineData("loja_online", "Loja online")]
    [InlineData("loja-online", "Loja online")]
    [InlineData("SQL_basico", "Sql basico")]
    public void HumanizeSlug_CapitalizaSoAPrimeiraLetra(string slug, string expected) =>
        DatasetDisplayNames.HumanizeSlug(slug).Should().Be(expected);

    [Fact]
    public void Resolve_PrefereONome_ECaiNoSlugHumanizado()
    {
        DatasetDisplayNames.Resolve("livraria", "Acervo da Livraria").Should().Be("Acervo da Livraria");
        DatasetDisplayNames.Resolve("livraria", "  ").Should().Be("Livraria");
        DatasetDisplayNames.Resolve("loja_online", null).Should().Be("Loja online");
        DatasetDisplayNames.Resolve(null, null).Should().BeNull();
    }
}
