using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task CompleteAsync_ChamaServicoComplete_NuncaSkip()
    {
        var stub = new RecordingProgress();
        var gateway = new LessonProgressGateway(stub, NullLogger<LessonProgressGateway>.Instance);
        await gateway.CompleteAsync(Guid.NewGuid(), "p", "m", "l", CancellationToken.None);
        stub.CompleteCalls.Should().Be(1);
        stub.SkipCalls.Should().Be(0);
    }

    [Fact]
    public async Task SkipAsync_ChamaServicoSkip()
    {
        var stub = new RecordingProgress();
        var gateway = new LessonProgressGateway(stub, NullLogger<LessonProgressGateway>.Instance);
        await gateway.SkipAsync(Guid.NewGuid(), "p", "m", "l", CancellationToken.None);
        stub.SkipCalls.Should().Be(1);
        stub.CompleteCalls.Should().Be(0);
    }

    private sealed class RecordingProgress : ILessonProgressService
    {
        public int CompleteCalls { get; private set; }
        public int SkipCalls { get; private set; }

        public Task<LessonAccessResult> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
            Task.FromResult(new LessonAccessResult(LessonAccessKind.Allowed));

        public Task<LessonAccessResult> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
            Task.FromResult(new LessonAccessResult(LessonAccessKind.Allowed));

        public Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
        {
            CompleteCalls++;
            return Task.FromResult(new LessonCompletionResult(LessonCompletionResultKind.RequirementNotMet));
        }

        public Task<LessonCompletionResult> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
        {
            SkipCalls++;
            return Task.FromResult(new LessonCompletionResult(LessonCompletionResultKind.RequirementNotMet));
        }
    }
}
