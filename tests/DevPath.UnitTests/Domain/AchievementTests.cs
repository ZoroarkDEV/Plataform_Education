using System.Text.Json;
using DevPath.Application.Achievements;
using DevPath.Application.CodeExecution;
using DevPath.Domain.Achievements;
using FluentAssertions;

namespace DevPath.UnitTests.Domain;

public class AchievementTests
{
    [Fact]
    public void PrimeiroSelect_TemIdFixo_TextoDoPO_ESlug()
    {
        var a = Achievement.PrimeiroSelect();

        a.Id.Should().Be(Guid.Parse("5e1ec700-0001-4000-8000-000000000001"));
        a.Id.Should().Be(AchievementIds.PrimeiroSelect);
        a.Title.Should().Be("Primeiro SELECT");
        a.Description.Should().Be("Você fez um banco de dados responder à sua primeira pergunta.");
        JsonDocument.Parse(a.Criteria).RootElement.GetProperty("slug").GetString().Should().Be("primeiro-select");
        AchievementIds.PrimeiroSelectSlug.Should().Be("primeiro-select");
    }

    [Fact]
    public void PrimeiroSelect_EhSempreOMesmo()
    {
        Achievement.PrimeiroSelect().Id.Should().Be(Achievement.PrimeiroSelect().Id);
    }

    [Fact]
    public void CreateWithId_UsaOId_ECreate_GeraNovo()
    {
        var id = Guid.NewGuid();
        Achievement.CreateWithId(id, " T ", " D ", "{}").Should().BeEquivalentTo(new { Id = id, Title = "T", Description = "D" });
        Achievement.Create("T", "D", "{}").Id.Should().NotBe(Achievement.Create("T", "D", "{}").Id);
    }

    [Fact]
    public void CreateWithId_IdVazio_Recusa()
    {
        var act = () => Achievement.CreateWithId(Guid.Empty, "T", "D", "{}");
        act.Should().Throw<ArgumentException>();
    }
}

public class ExecutionOutcomeAchievementTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PorPadrao_NewlyUnlockedAchievement_EhNulo()
    {
        ExecutionOutcome.Failure(ExecutionStatus.Fail, "x").NewlyUnlockedAchievement.Should().BeNull();
        ExecutionOutcome.AttemptsExhausted(2).NewlyUnlockedAchievement.Should().BeNull();
    }

    [Fact]
    public void Json_CamelCase_IdaEVoltaComoNoRedis()
    {
        var earned = new EarnedAchievementDto("Primeiro SELECT", "Você fez um banco de dados responder à sua primeira pergunta.",
            new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero));
        var outcome = ExecutionOutcome.Failure(ExecutionStatus.Pass, "ok") with { NewlyUnlockedAchievement = earned };

        var json = JsonSerializer.Serialize(outcome, Web);
        using var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("newlyUnlockedAchievement");
        node.GetProperty("title").GetString().Should().Be("Primeiro SELECT");
        node.GetProperty("description").GetString().Should().StartWith("Você fez");
        node.GetProperty("earnedAt").GetDateTimeOffset().Should().Be(earned.EarnedAt);

        JsonSerializer.Deserialize<ExecutionOutcome>(json, Web)!.NewlyUnlockedAchievement.Should().Be(earned);
    }
}
