using DevPath.Domain.Progress;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class UserProgressTests
{
    [Fact]
    public void Complete_MarcaComoConcluidoComNotaEData()
    {
        var progress = UserProgress.StartLesson(Guid.NewGuid(), Guid.NewGuid());
        progress.RegisterStudyTime(120);

        progress.Complete(score: 90);

        progress.Status.Should().Be(ProgressStatus.Completed);
        progress.Score.Should().Be(90);
        progress.TimeSpentSeconds.Should().Be(120);
        progress.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdateProgress_AcimaDe100_LancaExcecao()
    {
        var enrollment = UserPathEnrollment.Enroll(Guid.NewGuid(), Guid.NewGuid());

        var act = () => enrollment.UpdateProgress(101m, currentModuleId: null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
