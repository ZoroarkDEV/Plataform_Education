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

        names.Should().BeEquivalentTo(["title", "statementmarkdown", "language", "dataset", "startercode", "hints", "points"]);
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
