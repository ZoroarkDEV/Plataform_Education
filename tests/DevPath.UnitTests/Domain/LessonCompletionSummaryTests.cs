using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

/// <summary>#41: a frase de "o que você aprendeu" de cada aula (completion_summary no frontmatter).</summary>
public class LessonCompletionSummaryTests
{
    private static Lesson NewLesson() =>
        Lesson.Create(Guid.NewGuid(), "LIKE", ContentType.Markdown, "# LIKE", order: 1);

    [Fact]
    public void PorPadrao_NaoTemFrase()
    {
        NewLesson().CompletionSummary.Should().BeNull();
    }

    [Fact]
    public void Frase_EhAparada()
    {
        var lesson = NewLesson();
        lesson.SetCompletionSummary("  Achar um livro com LIKE.  ");
        lesson.CompletionSummary.Should().Be("Achar um livro com LIKE.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FraseVazia_LimpaOValor(string? summary)
    {
        var lesson = NewLesson();
        lesson.SetCompletionSummary("Achar um livro com LIKE.");

        lesson.SetCompletionSummary(summary);

        lesson.CompletionSummary.Should().BeNull();
    }

    [Fact]
    public void FraseCom200Caracteres_EhAceita_Com201_FalhaSemMudarAAtual()
    {
        var lesson = NewLesson();
        lesson.SetCompletionSummary(new string('a', Lesson.MaxCompletionSummaryLength));
        lesson.CompletionSummary.Should().HaveLength(200);

        var act = () => lesson.SetCompletionSummary(new string('b', Lesson.MaxCompletionSummaryLength + 1));

        act.Should().Throw<ArgumentException>();
        lesson.CompletionSummary.Should().Be(new string('a', 200));
    }

    [Fact]
    public void AtualizarOConteudo_NaoApagaAFrase()
    {
        var lesson = NewLesson();
        lesson.SetCompletionSummary("Achar um livro com LIKE.");

        lesson.UpdateContent("LIKE", ContentType.Markdown, "# LIKE novo", 2, 20, "sql", DifficultyLevel.Beginner);

        lesson.CompletionSummary.Should().Be("Achar um livro com LIKE.");
    }
}
