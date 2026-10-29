using DevPath.Domain.CareerPaths;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class ExerciseCommonErrorsAndPublishTests
{
    [Fact]
    public void Create_NascePublicado_SemErrosComunsESemLimiteDeTentativas()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]");

        exercise.IsPublished.Should().BeTrue();
        exercise.CommonErrors.Should().BeEmpty();
        exercise.MaxAttempts.Should().BeNull();
        exercise.Points.Should().Be(10, "10 pontos por exercício (confirmado pelo dono no #24)");
    }

    [Fact]
    public void Create_GuardaErrosComunsNaOrdemSemEspacos()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]",
            commonErrors: [new(" SELECT titulo livros; ", " Faltou o FROM. "), new("SELECT * FROM livro;", "A tabela é livros.")]);

        exercise.CommonErrors.Should().Equal(
            new CommonError("SELECT titulo livros;", "Faltou o FROM."), new CommonError("SELECT * FROM livro;", "A tabela é livros."));
    }

    [Theory]
    [InlineData("", "msg")]
    [InlineData("SELECT 1;", " ")]
    public void Update_ErroComumIncompleto_LancaENaoAlteraNada(string example, string message)
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]", commonErrors: [new("a", "b")]);

        var act = () => exercise.Update("Novo", "Enunciado", "sql", "[]", 10, "", "", null, commonErrors: [new(example, message)]);

        act.Should().Throw<ArgumentException>().WithParameterName("commonErrors");
        exercise.Title.Should().Be("Ex");
        exercise.CommonErrors.Should().Equal(new CommonError("a", "b"));
    }

    [Fact]
    public void Update_SemErrosComuns_MantemOsAtuais()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]", commonErrors: [new("a", "b")]);

        exercise.Update("Ex", "Enunciado", "sql", "[]", 10, "", "", null);

        exercise.CommonErrors.Should().ContainSingle();
    }

    [Fact]
    public void UnpublishEPublish_TrocamAVisibilidade()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]");

        exercise.Unpublish();
        exercise.IsPublished.Should().BeFalse();
        exercise.Publish();
        exercise.IsPublished.Should().BeTrue();
    }
}
