using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevPath.Application.Journey;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.CodeExecution;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace DevPath.IntegrationTests.Progress;

/// <summary>
/// Trilha mínima para a tela de conclusão com o serviço REAL (#27): Leitura (só leitura) → Notas (exercício) → Depois.
/// PostgreSQL + Redis do Testcontainers e o Worker real quando há Docker (como o <c>ExecutionApiFactory</c>).
/// </summary>
public class LessonProgressWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private IHost? _worker;
    private readonly string _queueKey = $"devpath:teste:{Guid.NewGuid():N}";

    /// <summary>Override em factory dedicada: exercício com MaxAttempts (fluxo AttemptsExhausted).</summary>
    protected virtual int? ExerciseMaxAttempts => null;

    public string PathSlug { get; private set; } = "";
    public string ModuleSlug { get; private set; } = "";
    public Guid ExerciseId { get; private set; }
    public Dictionary<string, Guid> LessonIds { get; } = new();

    public string Url(string lessonSlug) => $"/trilhas/{PathSlug}/{ModuleSlug}/{lessonSlug}";

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:DevPath"] = _postgres.GetConnectionString(),
        ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
        ["Execution:QueueKey"] = _queueKey,
        ["Execution:ResultTimeoutSeconds"] = "40",
        ["Sandbox:DefaultLimits:Timeout"] = "00:00:15",
        ["RateLimiting:Execution:PermitLimit"] = "1000"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in Settings())
        {
            builder.UseSetting(key, value);
        }
        builder.UseEnvironment("Testing");
    }

    public bool WorkerRunning => _worker is not null;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
            await db.Database.MigrateAsync();

            var path = CareerPath.Create($"Trilha conclusão {Guid.NewGuid():N}");
            var module = path.AddModule("SQL básico");
            module.Publish();
            var leitura = module.AddLesson("Leitura", ContentType.Markdown, "# Leitura\n\nTexto.");
            var notas = module.AddLesson("Notas", ContentType.Markdown, "# Notas\n\nTexto.");
            ExerciseId = notas.AddExercise(
                "Notas", "Liste nome e nota em ordem.", "sql", Execution.ExecutionApiFactory.TestCases,
                maxAttempts: ExerciseMaxAttempts).Id;
            var depois = module.AddLesson("Depois", ContentType.Markdown, "# Depois\n\nTexto.");
            db.CareerPaths.Add(path);
            await db.SaveChangesAsync();
            (PathSlug, ModuleSlug) = (path.Slug, module.Slug);
            foreach (var l in new[] { leitura, notas, depois }) LessonIds[l.Slug] = l.Id;
        }

        if (DockerAvailability.SkipReason is null)
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(Settings());
            builder.Services.AddSandboxExecution(builder.Configuration);
            _worker = builder.Build();
            await _worker.StartAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync();
            _worker.Dispose();
        }
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

/// <summary>
/// Tela da aula com o <c>LessonProgressGateway</c> + <c>ILessonProgressService</c> reais (#27), como o
/// <c>OnboardingRealServiceTests</c> do #11: nada de dublê entre a Web e o banco.
/// </summary>
public sealed class LessonProgressRealServiceTests(LessonProgressWebFactory factory) : IClassFixture<LessonProgressWebFactory>
{
    private const string Wrong = "SELECT nome FROM alunos;";
    private const string Right = "SELECT nome, nota FROM alunos ORDER BY nome;";

    [Fact]
    public async Task AulaDeLeitura_ConcluiNoClique_SemPontos_ELiberaAProxima()
    {
        var (client, userId) = await StudentAsync();
        var leitura = factory.Url("leitura");
        var notas = factory.Url("notas");

        (await client.GetAsync(notas)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Notas está bloqueada antes de concluir Leitura");

        var post = await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>());
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);

        var row = await ProgressAsync(userId, "leitura");
        row!.Status.Should().Be(ProgressStatus.Completed);
        row.CompletionKind.Should().Be(LessonCompletionKind.Read);
        row.Score.Should().BeNull();

        (await client.GetAsync(notas)).StatusCode.Should().Be(HttpStatusCode.OK, "Next desbloqueia");
        var html = await (await client.GetAsync(leitura)).ReadHtmlAsync();
        html.Should().Contain("data-completed-badge").And.Contain($"href=\"{notas}\"");
    }

    [Fact]
    public async Task DuploClique_ConcluiUmaVez_EAmbosRespondemSucesso()
    {
        var (client, userId) = await StudentAsync();
        var leitura = factory.Url("leitura");

        var first = await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>());
        var second = await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>());

        first.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);
        second.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        (await db.UserProgress.CountAsync(p => p.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task UrlDiretaDeAulaBloqueada_403()
    {
        var (client, _) = await StudentAsync();

        var response = await client.GetAsync(factory.Url("depois"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadHtmlAsync()).Should().Contain("Aula bloqueada").And.Contain("Notas");
    }

    [DockerFact]
    public async Task CincoFalhas_ConcluiSemSkip_MantemBloqueio_NaoGravaSkip()
    {
        var (client, userId) = await StudentAsync();
        await CompleteReadingAsync(client);
        var notas = factory.Url("notas");
        await AddAntiforgeryHeaderAsync(client);

        for (var i = 1; i <= 5; i++)
            await ExecuteRawAsync(client, Wrong);

        (await (await client.GetAsync(notas)).ReadHtmlAsync()).Should().Contain("data-skip-form", "CanSkip na 5ª falha");

        // "Concluí" sem skipWithoutPoints: não conclui, não grava SkippedAfterFailures.
        var complete = await client.SubmitFormAsync(notas, notas + "/concluir", new Dictionary<string, string>());
        complete.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);
        var html = await (await client.GetAsync(notas)).ReadHtmlAsync();
        html.Should().ContainAny(LessonCompletionTexts.RequirementNotMetFlash, LessonCompletionTexts.ExerciseBlocker);
        html.Should().Contain("data-skip-form");
        html.Should().NotContain(LessonCompletionTexts.BadgeVisible);

        var row = await ProgressAsync(userId, "notas");
        (row?.CompletionKind).Should().BeNull("CompleteAsync não grava skip");
    }

    [Fact]
    public async Task Dashboard_MostraSeloConcluida_NaAulaConcluida()
    {
        var (client, _) = await StudentAsync();
        var leitura = factory.Url("leitura");
        await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>());

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().MatchRegex(@"data-lesson-status=""Completed""[^>]*>\s*<span[^>]*data-completed-badge");
        html.Should().NotContain("+0");
        html.Should().MatchRegex(@"data-total-points>Seus pontos: 0</p>", "aula de leitura não vale pontos (#39)");
    }

    [DockerFact]
    public async Task PassPeloExecutar_ConcluiAAula_ComLessonCompletedTrue()
    {
        var (client, userId) = await StudentAsync();
        await CompleteReadingAsync(client);
        var page = await (await client.GetAsync(factory.Url("notas"))).ReadHtmlAsync();
        page.Should().Contain($"data-exercise-id=\"{factory.ExerciseId}\"", "o Executar usa o ExerciseDto.Id");

        await AddAntiforgeryHeaderAsync(client);
        var json = await ExecuteRawAsync(client, Right);

        json.GetProperty("status").GetString().Should().Be("pass");
        json.GetProperty("lessonCompleted").GetBoolean().Should().BeTrue("o JS lê lessonCompleted em camelCase");
        var row = await ProgressAsync(userId, "notas");
        row!.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        row.Score.Should().Be(10);

        var dashboard = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        dashboard.Should().Contain("data-total-points>Seus pontos: 10</p>", "o total vem do TotalPoints do servidor (#39)");
    }

    [DockerFact]
    public async Task CincoFalhas_LiberaSeguirSemPontos_ConcluiCom0_EPassDepoisPromove()
    {
        var (client, userId) = await StudentAsync();
        await CompleteReadingAsync(client);
        var notas = factory.Url("notas");
        await AddAntiforgeryHeaderAsync(client);

        for (var i = 1; i <= 4; i++)
        {
            (await ExecuteRawAsync(client, Wrong)).GetProperty("lessonCompleted").GetBoolean().Should().BeFalse();
        }
        (await (await client.GetAsync(notas)).ReadHtmlAsync()).Should().NotContain("data-skip-form", "antes da 5ª falha não há pulo");

        await ExecuteRawAsync(client, Wrong);
        (await (await client.GetAsync(notas)).ReadHtmlAsync()).Should().Contain("data-skip-form", "CanSkip na 5ª falha");

        var skip = await client.SubmitFormAsync(notas, notas + "/concluir", new Dictionary<string, string> { ["skipWithoutPoints"] = "true" });
        skip.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);
        var skipped = await ProgressAsync(userId, "notas");
        skipped!.CompletionKind.Should().Be(LessonCompletionKind.SkippedAfterFailures);
        skipped.Score.Should().Be(0);
        (await client.GetAsync(factory.Url("depois"))).StatusCode.Should().Be(HttpStatusCode.OK, "a próxima libera depois do pulo");

        var pass = await ExecuteRawAsync(client, Right);
        pass.GetProperty("lessonCompleted").GetBoolean().Should().BeTrue("o pass promove o pulo");
        var promoted = await ProgressAsync(userId, "notas");
        promoted!.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        promoted.Score.Should().Be(10);
    }

    // ---------- Ajudantes ----------

    private async Task CompleteReadingAsync(HttpClient client)
    {
        var leitura = factory.Url("leitura");
        var post = await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>());
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);
    }

    private async Task<JsonElement> ExecuteRawAsync(HttpClient client, string code)
    {
        factory.WorkerRunning.Should().BeTrue("o Worker sobe quando há Docker");
        var response = await client.PostAsJsonAsync($"/api/exercicios/{factory.ExerciseId}/executar", new { code });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AddAntiforgeryHeaderAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
        client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
    }

    private async Task<UserProgress?> ProgressAsync(Guid userId, string lessonSlug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        return await db.UserProgress.AsNoTracking()
            .Where(p => p.UserId == userId && p.LessonId == factory.LessonIds[lessonSlug])
            .SingleOrDefaultAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> StudentAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var email = $"{Guid.NewGuid():N}@devpath.test";
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Teste",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
        (await journey.EnrollAsync(userId, factory.PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return (client, userId);
    }
}


/// <summary>Mesma trilha da <see cref="LessonProgressWebFactory"/>, com MaxAttempts=2 no exercício.</summary>
public sealed class LessonProgressExhaustedWebFactory : LessonProgressWebFactory
{
    protected override int? ExerciseMaxAttempts => 2;
}

/// <summary>E2E: esgotar MaxAttempts → Concluí → AttemptsExhausted → Próxima liberada (também no reload).</summary>
public sealed class LessonProgressExhaustedRealServiceTests(LessonProgressExhaustedWebFactory factory)
    : IClassFixture<LessonProgressExhaustedWebFactory>
{
    private const string Wrong = "SELECT nome FROM alunos;";

    [DockerFact]
    public async Task Exhaust_MostraConcluir_ConcluiSemPontos_LiberaProxima_EPersisteNoReload()
    {
        var (client, userId) = await StudentAsync();
        var leitura = factory.Url("leitura");
        var notas = factory.Url("notas");
        var depois = factory.Url("depois");

        (await client.SubmitFormAsync(leitura, leitura + "/concluir", new Dictionary<string, string>()))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);

        await AddAntiforgeryHeaderAsync(client);
        for (var i = 1; i <= 2; i++)
        {
            var outcome = await ExecuteRawAsync(client, Wrong);
            outcome.GetProperty("lessonCompleted").GetBoolean().Should().BeFalse(
                "esgotar tentativas não conclui a aula sozinho");
            if (i == 2)
                outcome.GetProperty("remainingAttempts").GetInt32().Should().Be(0);
        }

        var afterFail = await (await client.GetAsync(notas)).ReadHtmlAsync();
        afterFail.Should().Contain("data-complete-form", "CanComplete + AttemptsExhausted mostra Concluí");
        afterFail.Should().Contain(LessonCompletionTexts.CompleteButton);
        afterFail.Should().NotContain("data-skip-form");
        afterFail.Should().NotContain($"href=\"{depois}\"");
        afterFail.Should().Contain(LessonCompletionTexts.AttemptsExhaustedReason);

        var conclusao = await (await client.GetAsync(notas + "/conclusao")).ReadHtmlAsync();
        conclusao.Should().Contain("data-complete-form");
        conclusao.Should().Contain(LessonCompletionTexts.CompleteButton);

        (await client.GetAsync(depois)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "ainda locked antes do Concluí");

        var post = await client.SubmitFormAsync(notas, notas + "/concluir", new Dictionary<string, string>());
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.SeeOther);

        var row = await ProgressAsync(userId, "notas");
        row!.Status.Should().Be(ProgressStatus.Completed);
        row.CompletionKind.Should().Be(LessonCompletionKind.AttemptsExhausted);
        row.Score.Should().Be(0);

        (await client.GetAsync(depois)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await (await client.GetAsync(notas)).ReadHtmlAsync();
        after.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after.Should().ContainAny(
            LessonCompletionTexts.AttemptsExhaustedJustCompleted,
            LessonCompletionTexts.AttemptsExhaustedReopen);
        after.Should().Contain($"href=\"{depois}\"");
        after.Should().NotContain("+0");

        var reload = await (await client.GetAsync(notas)).ReadHtmlAsync();
        reload.Should().Contain(LessonCompletionTexts.BadgeVisible);
        reload.Should().Contain($"href=\"{depois}\"");
        reload.Should().Contain(LessonCompletionTexts.AttemptsExhaustedReopen);
    }

    private async Task<JsonElement> ExecuteRawAsync(HttpClient client, string code)
    {
        factory.WorkerRunning.Should().BeTrue("o Worker sobe quando há Docker");
        var response = await client.PostAsJsonAsync($"/api/exercicios/{factory.ExerciseId}/executar", new { code });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AddAntiforgeryHeaderAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
        client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
    }

    private async Task<UserProgress?> ProgressAsync(Guid userId, string lessonSlug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        return await db.UserProgress.AsNoTracking()
            .Where(p => p.UserId == userId && p.LessonId == factory.LessonIds[lessonSlug])
            .SingleOrDefaultAsync();
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
            ["DisplayName"] = "Aluna Exhaust",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
        (await journey.EnrollAsync(userId, factory.PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return (client, userId);
    }
}
