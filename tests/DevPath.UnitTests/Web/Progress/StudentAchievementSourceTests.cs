using DevPath.Application.Achievements;
using DevPath.Domain.Achievements;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Progress;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Progress;

/// <summary>Adapter fino do IStudentAchievementService e o buffer de uma leitura do aviso (#45).</summary>
public sealed class StudentAchievementSourceTests
{
    private static readonly DateTimeOffset EarnedAt =
        new(2026, 11, 2, 13, 0, 0, TimeSpan.Zero);

    private static readonly EarnedAchievementDto Dto = new(
        AchievementTexts.FirstSelectTitle,
        AchievementTexts.FirstSelectDescription,
        EarnedAt);

    [Fact]
    public void ToAchievement_maps_the_dto_one_to_one()
    {
        var view = StudentAchievementSource.ToAchievement(Dto);
        view.Should().Be(new EarnedAchievement(Dto.Title, Dto.Description, Dto.EarnedAt));
        view.Title.Should().Be(AchievementIds.PrimeiroSelectTitle);
        view.Description.Should().Be(AchievementIds.PrimeiroSelectDescription);
        view.EarnedAt.Should().Be(EarnedAt);
    }

    [Fact]
    public async Task ListAsync_maps_every_item_in_order()
    {
        var later = Dto with { Title = "Outro", EarnedAt = EarnedAt.AddDays(1) };
        var source = new StudentAchievementSource(new StubService([Dto, later]));

        var list = await source.ListAsync(Guid.NewGuid());

        list.Should().Equal(
            StudentAchievementSource.ToAchievement(Dto),
            StudentAchievementSource.ToAchievement(later));
        (await source.GetNewlyUnlockedAsync(Guid.NewGuid())).Should().BeNull(
            "o aviso não vem do ListAsync: quem já tinha direito não vê Selo desbloqueado");
    }

    [Fact]
    public void Buffer_gives_the_badge_to_the_first_Take_only()
    {
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var buffer = new JustUnlockedAchievementBuffer();
        var badge = StudentAchievementSource.ToAchievement(Dto);

        buffer.Set(user, badge);

        buffer.Take(other).Should().BeNull();
        buffer.Take(user).Should().Be(badge);
        buffer.Take(user).Should().BeNull("um GET /conclusao seguinte não reexibe o aviso");
    }

    private sealed class StubService(IReadOnlyList<EarnedAchievementDto> items) : IStudentAchievementService
    {
        public Task<IReadOnlyList<EarnedAchievementDto>> ListAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(items);
    }
}
