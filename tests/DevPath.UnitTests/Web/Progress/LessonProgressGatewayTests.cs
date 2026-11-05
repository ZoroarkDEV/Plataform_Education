using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;
using DomainKind = DevPath.Domain.Progress.LessonCompletionKind;
using WebKind = DevPath.Web.Progress.LessonCompletionKind;

namespace DevPath.UnitTests.Web.Progress;

public sealed class LessonProgressGatewayTests
{
    [Fact]
    public void TodoLessonAccessKind_TemMapeamento() =>
        Enum.GetValues<LessonAccessKind>().Select(LessonProgressGateway.ToStatus).Should().OnlyHaveUniqueItems();

    [Fact]
    public void TodoLessonCompletionResultKind_TemMapeamento() =>
        Enum.GetValues<LessonCompletionResultKind>().Select(LessonProgressGateway.ToStatus).Should().OnlyHaveUniqueItems();

    [Theory]
    [InlineData(DomainKind.Read, WebKind.Read)]
    [InlineData(DomainKind.ExercisePassed, WebKind.ExercisePassed)]
    [InlineData(DomainKind.AttemptsExhausted, WebKind.AttemptsExhausted)]
    [InlineData(DomainKind.SkippedAfterFailures, WebKind.SkippedAfterFailures)]
    public void CompletionKind_PorNome(DomainKind from, WebKind to) => LessonProgressGateway.ToView(from).Should().Be(to);

    [Fact]
    public void Estado_MapeiaCampoACampo()
    {
        var a = new LessonRefDto("p", "m", "a", "A");
        var b = new LessonRefDto("p", "m2", "b", "B");
        var id = Guid.NewGuid();
        var state = new LessonProgressState(a, JourneyLessonStatus.Completed, null, DateTimeOffset.UnixEpoch,
            DomainKind.SkippedAfterFailures, 0, false, null,
            new LessonExerciseProgressDto(id, 10, false, 6, 6, null, 5),
            null, new DashboardLessonDto(b, JourneyLessonStatus.Available), 50, 30);

        var view = LessonProgressGateway.ToView(state);

        view.Lesson.Should().Be(new LessonRef("p", "m", "a", "A"));
        view.Status.Should().Be(LessonStatus.Completed);
        view.CompletionKind.Should().Be(WebKind.SkippedAfterFailures);
        view.Points.Should().Be(0);
        view.Exercise.Should().Be(new LessonExerciseProgressView(id, 10, false, 6, null, null, 6, true, 0));
        view.Next!.Lesson.Should().Be(new LessonRef("p", "m2", "b", "B"), "a paginação cruza módulos");
        view.PathProgressPercentage.Should().Be(50);
    }
}
