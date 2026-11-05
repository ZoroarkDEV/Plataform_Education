using DevPath.Application.CodeExecution;
using DevPath.Application.Progress;
using DevPath.Domain.Progress;
using FluentAssertions;

namespace DevPath.UnitTests.Progress;

/// <summary>Regra de conclusão de aula (Q1, Q3, Q5 do ADR-007), sem banco.</summary>
public class LessonCompletionRuleTests
{
    private static ExerciseAttemptsInput Ex(int used = 0, int failed = 0, bool passed = false, int points = 10,
        int? max = null, int? skipAfter = null) =>
        new(Guid.NewGuid(), points, max, skipAfter, used, failed, passed);

    [Fact]
    public void Padrao_CincoErros() => LessonCompletionRule.DefaultFailuresBeforeSkip.Should().Be(5);

    [Fact]
    public void AulaSoDeLeitura_ConcluiComReadSemPontos()
    {
        var d = LessonCompletionRule.Evaluate([]);

        d.Should().Be(new LessonCompletionDecision(true, LessonCompletionKind.Read, null, null));
    }

    [Fact]
    public void ExercicioPassou_ExercisePassedComPontosCheios_SemDescontoPorTentativas()
    {
        var d = LessonCompletionRule.Evaluate([Ex(used: 9, failed: 8, passed: true, points: 10)]);

        d.Should().Be(new LessonCompletionDecision(true, LessonCompletionKind.ExercisePassed, 10, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void PoucosErros_Bloqueia(int failed)
    {
        var d = LessonCompletionRule.Evaluate([Ex(used: failed, failed: failed)]);

        d.CanComplete.Should().BeFalse();
        d.Blocker.Should().Be(LessonCompletionBlocker.ExerciseNotPassed);
        d.Kind.Should().BeNull();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    public void CincoErrosOuMais_SeguirSemPontos(int failed)
    {
        var d = LessonCompletionRule.Evaluate([Ex(used: failed, failed: failed)]);

        d.Should().Be(new LessonCompletionDecision(true, LessonCompletionKind.SkippedAfterFailures, 0, null));
    }

    [Fact]
    public void LimiarPorExercicio_SobrescreveOPadrao()
    {
        LessonCompletionRule.Evaluate([Ex(used: 2, failed: 2, skipAfter: 2)]).Kind.Should().Be(LessonCompletionKind.SkippedAfterFailures);
        LessonCompletionRule.Evaluate([Ex(used: 5, failed: 5, skipAfter: 8)]).CanComplete.Should().BeFalse();
    }

    [Fact]
    public void TentativasEsgotadas_AttemptsExhausted_MesmoComMenosDeCincoErros()
    {
        var d = LessonCompletionRule.Evaluate([Ex(used: 2, failed: 2, max: 2)]);

        d.Should().Be(new LessonCompletionDecision(true, LessonCompletionKind.AttemptsExhausted, 0, null));
    }

    [Fact]
    public void DoisExercicios_UmPassouOutroDispensado_SomaSoOQuePassou()
    {
        var d = LessonCompletionRule.Evaluate([Ex(used: 1, passed: true, points: 10), Ex(used: 5, failed: 5, points: 20)]);

        d.Should().Be(new LessonCompletionDecision(true, LessonCompletionKind.SkippedAfterFailures, 10, null));
    }

    [Fact]
    public void DoisExercicios_UmAindaPendente_Bloqueia() =>
        LessonCompletionRule.Evaluate([Ex(used: 1, passed: true), Ex(used: 1, failed: 1)]).CanComplete.Should().BeFalse();

    [Fact]
    public void Describe_SemLimite_ContaQuantoFaltaParaPular()
    {
        var dto = LessonCompletionRule.Describe(Ex(used: 3, failed: 3));

        dto.MaxAttempts.Should().BeNull("sem limite: a tela diz 'execute quantas vezes quiser'");
        dto.RemainingAttempts.Should().BeNull();
        dto.FailedAttempts.Should().Be(3);
        dto.FailuresBeforeSkip.Should().Be(5);
        dto.FailuresUntilSkip.Should().Be(2);
        dto.CanSkip.Should().BeFalse();
        dto.AttemptsExhausted.Should().BeFalse();
    }

    [Fact]
    public void Describe_DepoisDoLimiar_PodePularEContinuaTentando()
    {
        var dto = LessonCompletionRule.Describe(Ex(used: 6, failed: 6));

        dto.CanSkip.Should().BeTrue();
        dto.FailuresUntilSkip.Should().Be(0);
        dto.AttemptsExhausted.Should().BeFalse("sem MaxAttempts ainda pode executar");
    }

    [Fact]
    public void Describe_Passou_NaoPulaNemEsgota()
    {
        var dto = LessonCompletionRule.Describe(Ex(used: 7, failed: 6, passed: true, max: 7));

        dto.CanSkip.Should().BeFalse();
        dto.AttemptsExhausted.Should().BeFalse();
        dto.FailuresUntilSkip.Should().Be(0);
        dto.RemainingAttempts.Should().Be(0);
    }
}

/// <summary>O que conta como tentativa errada (ADR-007, Decisões).</summary>
public class AttemptCountingTests
{
    [Theory]
    [InlineData(ExecutionStatus.Fail)]
    [InlineData(ExecutionStatus.Error)]
    [InlineData(ExecutionStatus.Timeout)]
    public void FailErroEDemora_SaoGravadosComoErro(ExecutionStatus status) =>
        AttemptCounting.ResultFor(status).Should().Be(SubmissionResult.Fail);

    [Fact]
    public void Pass_GravadoComoPass() => AttemptCounting.ResultFor(ExecutionStatus.Pass).Should().Be(SubmissionResult.Pass);

    [Fact]
    public void TentativasEsgotadas_NaoGravaNada()
    {
        var act = () => AttemptCounting.ResultFor(ExecutionStatus.AttemptsExhausted);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ErroETimeout_ContamParaOSeguirSemPontos()
    {
        var results = new[] { ExecutionStatus.Error, ExecutionStatus.Timeout, ExecutionStatus.Fail, ExecutionStatus.Error, ExecutionStatus.Timeout }
            .Select(AttemptCounting.ResultFor);

        var counts = AttemptCounting.Count(results);

        counts.Should().Be(new AttemptCounts(Used: 5, Failed: 5, Passed: false));
        LessonCompletionRule.Evaluate([new(Guid.NewGuid(), 10, null, null, counts.Used, counts.Failed, counts.Passed)])
            .Kind.Should().Be(LessonCompletionKind.SkippedAfterFailures);
    }

    [Fact]
    public void Count_SemNada_Zero() => AttemptCounting.Count([]).Should().Be(new AttemptCounts(0, 0, false));

    [Fact]
    public void Count_PassDepoisDeErros_PassouEErrosContinuamContados() =>
        AttemptCounting.Count([SubmissionResult.Fail, SubmissionResult.Fail, SubmissionResult.Pass])
            .Should().Be(new AttemptCounts(3, 2, true));
}
