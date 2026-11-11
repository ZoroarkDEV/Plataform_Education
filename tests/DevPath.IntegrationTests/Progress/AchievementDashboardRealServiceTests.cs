using System.Net;
using DevPath.Application.Journey;
using DevPath.Domain.Achievements;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Progress;

/// <summary>
/// Dashboard com o IStudentAchievementService real (#45): backfill silencioso, data do primeiro acerto,
/// sem aviso de "desbloqueado".
/// </summary>
public sealed class AchievementDashboardRealServiceTests(SeededContentWebFactory factory) : IClassFixture<SeededContentWebFactory>
{
    private const string PathSlug = "sql-dados";
    private static readonly DateTime FirstPassAt = new(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Dashboard_backfill_mostra_selo_com_a_data_do_primeiro_acerto_sem_aviso()
    {
        var (client, userId) = await StudentAsync();
        await SeedLesson1PassedAsync(userId, FirstPassAt);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().Contain("data-total-points>");
        html.Should().Contain("data-achievements");
        html.Should().Contain(AchievementIds.PrimeiroSelectTitle);
        html.Should().Contain(AchievementTexts.EarnedOn(new DateTimeOffset(FirstPassAt)));
        html.Should().Contain("Ganho em 2 de novembro de 2026");
        html.Should().Contain($"datetime=\"{AchievementTexts.FormatDateIso(new DateTimeOffset(FirstPassAt))}\"");
        html.Should().NotContain("Selo desbloqueado", "o backfill é silencioso: o aviso só sai no pass que concede");
        html.Should().NotContain("2026-11-02T").And.NotContain("Z</time>");
    }

    private async Task SeedLesson1PassedAsync(Guid userId, DateTime completedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var lessonId = await db.Lessons.AsNoTracking()
            .Where(l => l.Slug == AchievementIds.PrimeiroSelectBackfillLessonSlug)
            .Select(l => l.Id)
            .SingleAsync();
        var row = UserProgress.StartLesson(userId, lessonId);
        row.Complete(10, LessonCompletionKind.ExercisePassed);
        db.UserProgress.Add(row);
        await db.SaveChangesAsync();
        await db.UserProgress.Where(p => p.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.CompletedAt, completedAt)
            .SetProperty(p => p.LastAccessedAt, completedAt));
    }

    private async Task<(HttpClient Client, Guid UserId)> StudentAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });
        var email = $"{Guid.NewGuid():N}@devpath.test";
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Backfill",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
        (await journey.EnrollAsync(userId, PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return (client, userId);
    }
}
