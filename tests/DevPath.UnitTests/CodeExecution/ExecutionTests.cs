using DevPath.Application.CodeExecution;
using FluentAssertions;

namespace DevPath.UnitTests.CodeExecution;

public class LessonAccessPolicyTests
{
    private static readonly LessonAccessPolicy Policy = new();

    private static ExerciseVisibility Visible() =>
        new(Guid.NewGuid(), Guid.NewGuid(), PathActive: true, ModulePublished: true, LessonPublished: true, LessonDraft: false,
            ExercisePublished: true);

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
    public void ExercicioDespublicado_NotFound() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { ExercisePublished = false }).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void AulaEmRascunho_NotFound_NaoRevelaQueExiste() =>
        Policy.Evaluate(Guid.NewGuid(), Visible() with { LessonDraft = true }).Should().Be(ExerciseAccess.NotFound);

    [Fact]
    public void SoExistemDoisResultados() =>
        Enum.GetValues<ExerciseAccess>().Should().Equal(ExerciseAccess.Allowed, ExerciseAccess.NotFound);
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
    public void TentativasEsgotadas_StatusProprioMensagemEmPortuguesERestantesZero(int max, string start)
    {
        var outcome = ExecutionOutcome.AttemptsExhausted(max);

        outcome.Status.Should().Be(ExecutionStatus.AttemptsExhausted);
        outcome.FriendlyMessage.Should().StartWith(start);
        outcome.RemainingAttempts.Should().Be(0);
        outcome.SubmissionId.Should().BeNull();
    }

    [Theory]
    [InlineData(ExecutionStatus.Pass, "pass")]
    [InlineData(ExecutionStatus.Fail, "fail")]
    [InlineData(ExecutionStatus.Error, "error")]
    [InlineData(ExecutionStatus.Timeout, "timeout")]
    [InlineData(ExecutionStatus.AttemptsExhausted, "attempts_exhausted")]
    public void Status_SerializaComoOContratoDoFrontend(ExecutionStatus status, string json) =>
        System.Text.Json.JsonSerializer.Serialize(status).Should().Be($"\"{json}\"");
}
