using DevPath.Application.CodeExecution;
using FluentAssertions;

namespace DevPath.UnitTests.CodeExecution;

public class LessonAccessPolicyTests
{
    private static readonly LessonAccessPolicy Policy = new();

    private static ExerciseVisibility Visible() =>
        new(Guid.NewGuid(), Guid.NewGuid(), PathActive: true, ModulePublished: true, LessonPublished: true, LessonDraft: false);

    [Fact]
    public void AulaVisivelEPronta_Libera() =>
        Policy.Evaluate(Guid.NewGuid(), Visible()).Should().Be(ExerciseAccess.Allowed);

    [Fact]
    public void ExercicioInexistente_NotFound() =>
        Policy.Evaluate(Guid.NewGuid(), null).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void TrilhaInativa_NotFound() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { PathActive = false }).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void ModuloNaoPublicado_NotFound() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { ModulePublished = false }).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void AulaNaoPublicada_NotFound() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { LessonPublished = false }).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void AulaEmRascunho_Draft() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { LessonDraft = true }).Should().Be(ExerciseAccess.Draft);

    [Fact]
    public void RascunhoEscondido_NotFoundGanhaDeDraft() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { LessonDraft = true, LessonPublished = false })
            .Should().Be(ExerciseAccess.NotFound, "não revela que existe uma aula escondida");
}

public class ExecutionLimitsTests
{
    private static QueryResult Numbers(int count, bool truncatedByRunner = false) =>
        new(["n"], Enumerable.Range(1, count).Select(i => (IReadOnlyList<SqlValue>)[SqlValue.Number(i)]).ToList(), truncatedByRunner);

    [Fact]
    public void Ate200Linhas_NaoCorta()
    {
        var (rows, truncated) = ExecutionLimits.CapRows(Numbers(200));

        rows.Should().HaveCount(200);
        truncated.Should().BeFalse();
    }

    [Fact]
    public void MaisDe200Linhas_CortaEMarcaTruncated()
    {
        var (rows, truncated) = ExecutionLimits.CapRows(Numbers(201));

        rows.Should().HaveCount(ExecutionLimits.MaxRowsShown);
        rows[^1].Should().Equal("200");
        truncated.Should().BeTrue();
    }

    [Fact]
    public void RunnerJaCortou_ContinuaTruncated()
    {
        var (rows, truncated) = ExecutionLimits.CapRows(Numbers(5, truncatedByRunner: true));

        rows.Should().HaveCount(5);
        truncated.Should().BeTrue();
    }

    [Fact]
    public void LimitePersonalizado_EValoresViramTextoOuNull()
    {
        var result = new QueryResult(["nome", "ativo", "nota"],
            [[SqlValue.Text("Ana"), SqlValue.Bool(true), SqlValue.Null], [SqlValue.Text("Bia"), SqlValue.Bool(false), SqlValue.Number(9.5m)]],
            false);

        var (rows, truncated) = ExecutionLimits.CapRows(result, maxRows: 1);

        rows.Should().ContainSingle().Which.Should().Equal("Ana", "true", null);
        truncated.Should().BeTrue();
    }

    [Fact]
    public void SemLinhas_ListaVazia()
    {
        var (rows, truncated) = ExecutionLimits.CapRows(Numbers(0));

        rows.Should().BeEmpty();
        truncated.Should().BeFalse();
    }

    [Fact]
    public void Falha_NaoTemTabela()
    {
        var outcome = ExecutionOutcome.Failure(ExecutionStatus.Timeout, "demorou", remainingAttempts: 2);

        outcome.Columns.Should().BeEmpty();
        outcome.Rows.Should().BeEmpty();
        outcome.TestCases.Should().BeEmpty();
        outcome.SubmissionId.Should().BeNull();
        outcome.RemainingAttempts.Should().Be(2);
    }

    [Theory]
    [InlineData(1, "Você já usou a única tentativa deste exercício.")]
    [InlineData(3, "Você já usou as 3 tentativas deste exercício.")]
    public void TentativasEsgotadas_MensagemEmPortugues(int max, string message) =>
        new AttemptsExhaustedException(max).Message.Should().Be(message);
}
