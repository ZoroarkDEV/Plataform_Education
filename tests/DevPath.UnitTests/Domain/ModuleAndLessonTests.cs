using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class ModuleAndLessonTests
{
    [Fact]
    public void Module_GeraSlugSemAcentos()
    {
        var module = Module.Create(Guid.NewGuid(), "Introdução ao C#", order: 1);

        module.Slug.Should().Be("introducao-ao-c");
    }

    [Fact]
    public void Lesson_NascePublicada_EPodeSerDespublicadaERepublicada()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), "Joins", ContentType.Markdown, "# Joins", order: 1);
        lesson.IsPublished.Should().BeTrue();

        lesson.Unpublish();
        lesson.IsPublished.Should().BeFalse();

        lesson.Publish();
        lesson.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Lesson_UpdateContent_NaoMudaIsPublished()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), "Joins", ContentType.Markdown, "# Joins", order: 1);
        lesson.Unpublish();

        lesson.UpdateContent("Joins", ContentType.Markdown, "# Joins v2", order: 1001, estimatedMinutes: 0,
            codeLanguage: null, difficulty: DifficultyLevel.Beginner);

        lesson.IsPublished.Should().BeFalse("reordenar ou editar não pode republicar uma aula removida");
    }

    [Fact]
    public void Lesson_GeraSlugSemAcentos()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), "Variáveis e Tipos", ContentType.Markdown, "# Aula", order: 1);

        lesson.Slug.Should().Be("variaveis-e-tipos");
    }

    [Fact]
    public void Module_TituloSoComSimbolos_LancaArgumentException()
    {
        var act = () => Module.Create(Guid.NewGuid(), "???", order: 1);

        act.Should().Throw<ArgumentException>().WithParameterName("title");
    }

    [Fact]
    public void CareerPath_UpdateDetails_MudaDadosMasMantemOSlug()
    {
        var path = CareerPath.Create("Backend .NET");

        path.UpdateDetails("Backend com .NET 9", "Nova descrição", DifficultyLevel.Intermediate, estimatedHours: 120, order: 2, icon: "dotnet");

        path.Title.Should().Be("Backend com .NET 9");
        path.Description.Should().Be("Nova descrição");
        path.DifficultyLevel.Should().Be(DifficultyLevel.Intermediate);
        path.EstimatedHours.Should().Be(120);
        path.Slug.Should().Be("backend-net", "a URL não muda quando o título é editado");
    }

    [Fact]
    public void ChangeSlug_TrocaENormalizaOSlug()
    {
        var path = CareerPath.Create("Backend .NET");

        path.ChangeSlug("Backend DotNet");

        path.Slug.Should().Be("backend-dotnet");
    }

    [Fact]
    public void Module_UpdateDetails_ValidaComoNoCreate()
    {
        var module = Module.Create(Guid.NewGuid(), "Fundamentos", order: 1);

        module.UpdateDetails("Fundamentos de C#", "Tipos e controle de fluxo", order: 3, estimatedMinutes: 90);
        var invalid = () => module.UpdateDetails("Fundamentos", "", order: -1, estimatedMinutes: 0);

        module.Title.Should().Be("Fundamentos de C#");
        module.Order.Should().Be(3);
        module.Slug.Should().Be("fundamentos");
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Lesson_UpdateContent_AtualizaEMantemOSlug()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), "SELECT básico", ContentType.Markdown, "v1", order: 1);

        lesson.UpdateContent("SELECT básico (revisado)", ContentType.Interactive, "v2", order: 2,
            estimatedMinutes: 15, codeLanguage: "sql", difficulty: DifficultyLevel.Beginner);

        lesson.Content.Should().Be("v2");
        lesson.ContentType.Should().Be(ContentType.Interactive);
        lesson.CodeLanguage.Should().Be("sql");
        lesson.Slug.Should().Be("select-basico");
    }

    [Fact]
    public void AddExercise_RepassaStarterSolutionEMaxAttempts()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), "Loops", ContentType.Exercise, "# Loops", order: 1);

        var exercise = lesson.AddExercise("Soma de 1 a N", "Some de 1 até N.", "CSharp", "[]",
            points: 20, starterCode: "int Soma(int n) { }", solutionCode: "int Soma(int n) => n * (n + 1) / 2;", maxAttempts: 3);

        exercise.LessonId.Should().Be(lesson.Id);
        exercise.Language.Should().Be("csharp");
        exercise.Points.Should().Be(20);
        exercise.StarterCode.Should().Be("int Soma(int n) { }");
        exercise.SolutionCode.Should().Be("int Soma(int n) => n * (n + 1) / 2;");
        exercise.MaxAttempts.Should().Be(3);
        lesson.Exercises.Should().ContainSingle();
    }

    [Fact]
    public void Exercise_Update_ComMaxAttemptsZero_LancaExcecao()
    {
        var exercise = Exercise.Create(Guid.NewGuid(), "Ex", "Enunciado", "sql", "[]");

        var act = () => exercise.Update("Ex", "Enunciado", "sql", "[]", points: 10, starterCode: "", solutionCode: "", maxAttempts: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
