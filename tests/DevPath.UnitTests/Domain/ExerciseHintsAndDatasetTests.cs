using DevPath.Domain.CareerPaths;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class ExerciseHintsAndDatasetTests
{
    [Fact]
    public void Create_GuardaDicasNaOrdemSemEspacosEDatasetNormalizado()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]",
            dataset: "  livraria ", hints: ["  Geral ", "Direta"]);

        exercise.Hints.Should().Equal("Geral", "Direta");
        exercise.Dataset.Should().Be("livraria");
    }

    [Fact]
    public void Create_SemDicasNemDataset_UsaListaVaziaENulo()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]");

        exercise.Hints.Should().BeEmpty();
        exercise.Dataset.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_DicaVazia_LancaENaoAlteraNada(string badHint)
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]", hints: ["Antiga"]);

        var act = () => exercise.Update("Novo", "Enunciado", "sql", "[]", 10, "", "", null, "livraria", ["Ok", badHint]);

        act.Should().Throw<ArgumentException>().WithParameterName("hints");
        exercise.Title.Should().Be("Ex");
        exercise.Hints.Should().Equal("Antiga");
    }

    [Fact]
    public void Update_HintsNulo_MantemAsDicasAtuais()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]", hints: ["Antiga"]);

        exercise.Update("Ex", "Enunciado", "sql", "[]", 10, "", "", null);

        exercise.Hints.Should().Equal("Antiga");
    }

    [Fact]
    public void Update_DatasetComMaisDe100Caracteres_Lanca()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]");

        var act = () => exercise.Update("Ex", "Enunciado", "sql", "[]", 10, "", "", null, new string('a', Exercise.MaxDatasetLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("dataset");
    }
}
