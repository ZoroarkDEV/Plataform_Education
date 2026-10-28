using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class LessonObjectivesAndDraftTests
{
    private static Lesson NewLesson(IEnumerable<string>? objectives = null, bool isDraft = false) =>
        Lesson.Create(Guid.NewGuid(), "Joins", ContentType.Markdown, "# Joins", order: 1,
            learningObjectives: objectives, isDraft: isDraft);

    private static void Reorder(Lesson lesson, int order) =>
        lesson.UpdateContent(lesson.Title, lesson.ContentType, lesson.Content, order, lesson.EstimatedMinutes,
            lesson.CodeLanguage, lesson.Difficulty);

    [Fact]
    public void PorPadrao_SemObjetivos_ENaoERascunho()
    {
        var lesson = NewLesson();
        lesson.LearningObjectives.Should().BeEmpty();
        lesson.IsDraft.Should().BeFalse();
    }

    [Fact]
    public void Objetivos_SaoAparadosEMantemAOrdem()
    {
        var lesson = NewLesson(["  Usar INNER JOIN ", "Entender LEFT JOIN"]);
        lesson.LearningObjectives.Should().Equal("Usar INNER JOIN", "Entender LEFT JOIN");
    }

    [Fact]
    public void Objetivo_Com200Caracteres_EhAceito()
    {
        var lesson = NewLesson([new string('a', Lesson.MaxObjectiveLength)]);
        lesson.LearningObjectives.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Objetivo_Vazio_EhRejeitado(string objective)
    {
        var act = () => NewLesson([objective]);
        act.Should().Throw<ArgumentException>().WithParameterName("learningObjectives");
    }

    [Fact]
    public void Objetivo_Com201Caracteres_EhRejeitado()
    {
        var act = () => NewLesson([new string('a', Lesson.MaxObjectiveLength + 1)]);
        act.Should().Throw<ArgumentException>().WithParameterName("learningObjectives");
    }

    [Fact]
    public void UpdateContent_Invalido_NaoAlteraNada()
    {
        var lesson = NewLesson(["Objetivo"]);
        var act = () => lesson.UpdateContent("Outro título", ContentType.Markdown, "# novo", 2, 0, null,
            DifficultyLevel.Beginner, [new string('a', 201)]);

        act.Should().Throw<ArgumentException>();
        lesson.Title.Should().Be("Joins");
        lesson.Order.Should().Be(1);
        lesson.LearningObjectives.Should().Equal("Objetivo");
    }

    [Fact]
    public void UpdateContent_SemObjetivosNemRascunho_MantemOsValores()
    {
        // É o que o seed faz ao reordenar: não pode apagar objetivos nem tirar o rascunho.
        var lesson = NewLesson(["Objetivo"], isDraft: true);
        Reorder(lesson, 1001);

        lesson.Order.Should().Be(1001);
        lesson.LearningObjectives.Should().Equal("Objetivo");
        lesson.IsDraft.Should().BeTrue();
    }

    [Fact]
    public void UpdateContent_ComListaVazia_LimpaOsObjetivos()
    {
        var lesson = NewLesson(["Objetivo"]);
        lesson.UpdateContent("Joins", ContentType.Markdown, "# Joins", 1, 0, null, DifficultyLevel.Beginner, []);
        lesson.LearningObjectives.Should().BeEmpty();
    }

    [Fact]
    public void MarkAsDraft_EMarkAsReady_AlternamORascunho()
    {
        var lesson = NewLesson();
        lesson.MarkAsDraft();
        lesson.IsDraft.Should().BeTrue();
        lesson.MarkAsReady();
        lesson.IsDraft.Should().BeFalse();
    }

    [Fact]
    public void Module_AddLesson_RepassaObjetivosERascunho()
    {
        var module = Module.Create(Guid.NewGuid(), "Fundamentos", order: 1);
        var lesson = module.AddLesson("Joins", ContentType.Markdown, "# Joins", learningObjectives: ["Usar JOIN"], isDraft: true);

        lesson.LearningObjectives.Should().Equal("Usar JOIN");
        lesson.IsDraft.Should().BeTrue();
    }
}
