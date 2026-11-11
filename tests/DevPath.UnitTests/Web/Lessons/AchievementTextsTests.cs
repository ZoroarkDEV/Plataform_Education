using DevPath.Web.Models.Lessons;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Lessons;

/// <summary>Textos e data do selo "Primeiro SELECT" (#45, US-BADGE.1).</summary>
public sealed class AchievementTextsTests
{
    [Fact]
    public void Unlock_texts_match_the_PO()
    {
        AchievementTexts.Medal.Should().Be("🏅");
        AchievementTexts.FirstSelectTitle.Should().Be("Primeiro SELECT");
        AchievementTexts.FirstSelectDescription.Should().Be(
            "Você fez um banco de dados responder à sua primeira pergunta.");
        AchievementTexts.FirstSelectUnlockNotice.Should().Be(
            "Selo desbloqueado: Primeiro SELECT. Você fez um banco de dados responder à sua primeira pergunta.");
        AchievementTexts.SectionHeading.Should().Be("Seus selos");
        AchievementTexts.UnlockNotice(AchievementTexts.FirstSelectTitle, AchievementTexts.FirstSelectDescription)
            .Should().Be(AchievementTexts.FirstSelectUnlockNotice);
    }

    [Fact]
    public void FormatDate_uses_Sao_Paulo_and_Portuguese()
    {
        // 15:00 UTC = 12:00 em America/Sao_Paulo (UTC-3, sem horário de verão).
        var earned = new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
        AchievementTexts.FormatDate(earned).Should().Be("11 de novembro de 2026");
        AchievementTexts.FormatDateIso(earned).Should().Be("2026-11-11");
        AchievementTexts.EarnedOn(earned).Should().Be("Ganho em 11 de novembro de 2026");
        AchievementTexts.EarnedOn(earned).Should().NotContain("Z").And.NotContain("UTC").And.NotContain("T15");
    }

    [Fact]
    public void FormatDate_converts_from_UTC_across_midnight()
    {
        // 01:00 UTC do dia 11 = 22:00 do dia 10 em São Paulo.
        var earned = new DateTimeOffset(2026, 11, 11, 1, 0, 0, TimeSpan.Zero);
        AchievementTexts.FormatDate(earned).Should().Be("10 de novembro de 2026");
        AchievementTexts.FormatDateIso(earned).Should().Be("2026-11-10");
    }

    [Fact]
    public async Task Empty_source_returns_no_badges_and_never_unlocks()
    {
        var source = EmptyStudentAchievementSource.Instance;
        (await source.ListAsync(Guid.NewGuid())).Should().BeEmpty();
        (await source.GetNewlyUnlockedAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public void Dashboard_hides_badges_when_the_list_is_missing_or_empty()
    {
        var noProp = new DashboardViewModel("sql-dados", "SQL & Dados", 0, 5, 0, null, true, []);
        noProp.HasAchievements.Should().BeFalse();
        noProp.EarnedAchievements.Should().BeEmpty();

        var empty = noProp with { Achievements = [] };
        empty.HasAchievements.Should().BeFalse();
        empty.EarnedAchievements.Should().BeEmpty();
    }

    [Fact]
    public void Dashboard_exposes_earned_badges()
    {
        var badge = new EarnedAchievement(
            AchievementTexts.FirstSelectTitle,
            AchievementTexts.FirstSelectDescription,
            new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero));
        var dashboard = new DashboardViewModel("sql-dados", "SQL & Dados", 1, 5, 20, null, false, [], 10, [badge]);
        dashboard.HasAchievements.Should().BeTrue();
        dashboard.EarnedAchievements.Should().ContainSingle()
            .Which.Title.Should().Be(AchievementTexts.FirstSelectTitle);
        dashboard.TotalPointsText.Should().Be("Seus pontos: 10");
    }
}
