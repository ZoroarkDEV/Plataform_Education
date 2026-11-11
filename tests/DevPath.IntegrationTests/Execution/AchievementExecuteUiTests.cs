using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevPath.Application.CodeExecution;
using DevPath.Application.Journey;
using DevPath.Domain.Achievements;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.IntegrationTests.CodeExecution;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Execution;

/// <summary>
/// Aviso "Selo desbloqueado" no GET /conclusao depois do Executar, com o serviço real (#45).
/// Só o pass que acaba de gravar o selo preenche newlyUnlockedAchievement; um GET seguinte não reexibe.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class AchievementExecuteUiTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string PassSql = "SELECT nome, nota FROM alunos ORDER BY nome;";

    // A aula "SELECT" da factory tem dois exercícios, então um pass não conclui a aula e o
    // /conclusao não entra em JustPassed. "WHERE" tem um exercício só (como a aula 1 real).
    private string ConclusaoUrl =>
        $"/trilhas/{factory.PathSlug}/{factory.ModuleSlug}/{factory.WhereLessonSlug}/conclusao";

    private static string ExecuteUrl(Guid exerciseId) => $"/api/exercicios/{exerciseId}/executar";

    [DockerFact]
    public async Task PrimeiroAcerto_MostraAvisoNoConclusao_ESomeNoGetSeguinte()
    {
        var (client, userId) = await StudentAsync();
        await UnlockWhereAsync(userId);

        var outcome = await ExecuteAsync(client, factory.WhereExerciseId, PassSql);
        outcome.Status.Should().Be(ExecutionStatus.Pass, outcome.FriendlyMessage);
        outcome.LessonCompleted.Should().BeTrue();
        outcome.NewlyUnlockedAchievement.Should().NotBeNull();
        outcome.NewlyUnlockedAchievement!.Title.Should().Be(AchievementIds.PrimeiroSelectTitle);
        outcome.NewlyUnlockedAchievement.Description.Should().Be(AchievementIds.PrimeiroSelectDescription);

        var first = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        first.Should().Contain("data-achievement-unlocked");
        first.Should().Contain($"<strong>{AchievementIds.PrimeiroSelectTitle}</strong>");
        first.Should().Contain(AchievementIds.PrimeiroSelectDescription);
        first.Should().Contain("data-completion-message");

        var second = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        second.Should().NotContain("data-achievement-unlocked");
        second.Should().NotContain("Selo desbloqueado", "o buffer é de uma leitura: um GET seguinte não reexibe");

        var dashboard = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        dashboard.Should().Contain("data-achievements");
        dashboard.Should().Contain(AchievementIds.PrimeiroSelectTitle);
        dashboard.Should().Contain("Ganho em ");
        dashboard.Should().NotContain("Selo desbloqueado");
    }

    [DockerFact]
    public async Task QuemJaTinhaDireito_PassaSemAviso_EDashboardMostraADataDoPrimeiroAcerto()
    {
        var oldPass = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        var (client, userId) = await StudentAsync();
        await SeedOldPassAsync(userId, oldPass);
        await UnlockWhereAsync(userId);

        var outcome = await ExecuteAsync(client, factory.WhereExerciseId, PassSql);
        outcome.Status.Should().Be(ExecutionStatus.Pass, outcome.FriendlyMessage);
        outcome.NewlyUnlockedAchievement.Should().BeNull(
            "já tinha uma tentativa Pass: o Backend grava o selo em silêncio e não avisa");

        var conclusao = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        conclusao.Should().NotContain("data-achievement-unlocked");
        conclusao.Should().NotContain("Selo desbloqueado");

        var dashboard = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        dashboard.Should().Contain("data-achievements");
        dashboard.Should().Contain(AchievementIds.PrimeiroSelectTitle);
        dashboard.Should().Contain(AchievementTexts.EarnedOn(new DateTimeOffset(oldPass)));
        dashboard.Should().Contain("Ganho em 3 de novembro de 2026");
        dashboard.Should().NotContain("Selo desbloqueado");
    }

    [DockerFact]
    public async Task Refazer_NaoMostraAvisoDeNovo()
    {
        var (client, userId) = await StudentAsync();
        await UnlockWhereAsync(userId);

        var first = await ExecuteAsync(client, factory.WhereExerciseId, PassSql);
        first.NewlyUnlockedAchievement.Should().NotBeNull();
        first.LessonCompleted.Should().BeTrue();
        await client.GetAsync(ConclusaoUrl); // consome o aviso

        var redo = await ExecuteAsync(client, factory.WhereExerciseId, PassSql);
        redo.Status.Should().Be(ExecutionStatus.Pass, redo.FriendlyMessage);
        redo.NewlyUnlockedAchievement.Should().BeNull();

        var html = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        html.Should().NotContain("data-achievement-unlocked");
        html.Should().NotContain("Selo desbloqueado");
    }

    private async Task UnlockWhereAsync(Guid userId)
    {
        // Conclui a aula SELECT como leitura: libera o WHERE e NÃO dá direito silencioso ao selo
        // (o backfill só olha seu-primeiro-select com ExercisePassed, ou uma tentativa Pass).
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var progress = UserProgress.StartLesson(userId, factory.SelectLessonId);
        progress.Complete(null, LessonCompletionKind.Read);
        db.UserProgress.Add(progress);
        await db.SaveChangesAsync();
    }

    private async Task SeedOldPassAsync(Guid userId, DateTime submittedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var submission = CodeSubmission.Create(userId, factory.ExerciseId, "SELECT 1;", SubmissionResult.Pass, 1);
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync();
        await db.CodeSubmissions.Where(s => s.Id == submission.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SubmittedAt, submittedAt));
    }

    private async Task<ExecutionOutcome> ExecuteAsync(HttpClient client, Guid exerciseId, string code)
    {
        factory.WorkerRunning.Should().BeTrue("o Worker sobe quando há Docker");
        var response = await client.PostAsJsonAsync(ExecuteUrl(exerciseId), new { code });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExecutionOutcome>(Json))!;
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
            ["DisplayName"] = "Aluna Selo Real",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
            (await journey.EnrollAsync(userId, factory.PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        }

        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
        client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
        return (client, userId);
    }
}
