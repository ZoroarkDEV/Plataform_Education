using DevPath.Application.CodeExecution;
using DevPath.Application.Progress;
using DevPath.Domain.Progress;
using FluentAssertions;

namespace DevPath.UnitTests.Progress;

/// <summary>#43: cada execução gravada guarda certo, errado ou erro, sem mudar o Result que as regras usam.</summary>
public class AttemptOutcomeTests
{
    [Theory]
    [InlineData(ExecutionStatus.Pass, SubmissionOutcome.Pass)]
    [InlineData(ExecutionStatus.Fail, SubmissionOutcome.Fail)]
    [InlineData(ExecutionStatus.Error, SubmissionOutcome.Error)]
    [InlineData(ExecutionStatus.Timeout, SubmissionOutcome.Timeout)]
    public void OutcomeFor_SeparaCertoErradoErroETimeout(ExecutionStatus status, SubmissionOutcome expected)
    {
        AttemptCounting.OutcomeFor(status).Should().Be(expected);

        var submission = CodeSubmission.Create(Guid.NewGuid(), Guid.NewGuid(), "SELECT 1;",
            AttemptCounting.ResultFor(status), 1, outcome: AttemptCounting.OutcomeFor(status));
        submission.Outcome.Should().Be(expected);
        submission.Result.Should().Be(status == ExecutionStatus.Pass ? SubmissionResult.Pass : SubmissionResult.Fail);
    }

    [Theory]
    [InlineData(ExecutionStatus.AttemptsExhausted)]
    public void OutcomeFor_StatusQueNaoGrava_Lanca(ExecutionStatus status)
    {
        var act = () => AttemptCounting.OutcomeFor(status);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_SemOutcome_UsaOResult()
    {
        CodeSubmission.Create(Guid.NewGuid(), Guid.NewGuid(), "x", SubmissionResult.Pass, 1).Outcome.Should().Be(SubmissionOutcome.Pass);
        CodeSubmission.Create(Guid.NewGuid(), Guid.NewGuid(), "x", SubmissionResult.Fail, 1).Outcome.Should().Be(SubmissionOutcome.Fail);
    }

    [Theory]
    [InlineData(SubmissionResult.Pass, SubmissionOutcome.Error)]
    [InlineData(SubmissionResult.Fail, SubmissionOutcome.Pass)]
    public void Create_OutcomeQueContradizOResult_Lanca(SubmissionResult result, SubmissionOutcome outcome)
    {
        var act = () => CodeSubmission.Create(Guid.NewGuid(), Guid.NewGuid(), "x", result, 1, outcome: outcome);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_SeguirSemPontos_GravaSkippedAt_EAPromocaoMantem()
    {
        var progress = UserProgress.StartLesson(Guid.NewGuid(), Guid.NewGuid());
        progress.SkippedAt.Should().BeNull();

        progress.Complete(0, LessonCompletionKind.SkippedAfterFailures);
        var skippedAt = progress.SkippedAt;
        skippedAt.Should().NotBeNull();

        progress.Complete(10, LessonCompletionKind.ExercisePassed);
        progress.SkippedAt.Should().Be(skippedAt, "o histórico de quem usou o Seguir sem pontos não some com o acerto");
    }

    [Fact]
    public void Complete_SemSeguirSemPontos_NaoGravaSkippedAt()
    {
        var progress = UserProgress.StartLesson(Guid.NewGuid(), Guid.NewGuid());
        progress.Complete(10, LessonCompletionKind.ExercisePassed);
        progress.SkippedAt.Should().BeNull();
    }
}
