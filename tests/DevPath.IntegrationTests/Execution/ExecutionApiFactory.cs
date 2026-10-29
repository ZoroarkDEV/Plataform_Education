using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.CodeExecution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace DevPath.IntegrationTests.Execution;

/// <summary>
/// Sobe o sistema inteiro da execução de exercícios: a Web (WebApplicationFactory), um PostgreSQL e um
/// Redis do Testcontainers, e o laço REAL do Worker (<c>AddSandboxExecution</c>) num host no mesmo processo.
/// </summary>
/// <remarks>
/// O Worker só sobe quando há Docker e a imagem do sandbox; sem isso, os testes que dependem dele são
/// [DockerFact] e ficam "Skipped". A Web nunca recebe o sandbox (é isso que um dos testes confere).
/// </remarks>
public sealed class ExecutionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private IHost? _worker;

    /// <summary>Fila só desta execução de testes (não mistura com outra suíte no mesmo Redis).</summary>
    public string QueueKey { get; } = $"devpath:teste:{Guid.NewGuid():N}";

    public Guid ExerciseId { get; private set; }
    public Guid LimitedExerciseId { get; private set; }
    public Guid DraftExerciseId { get; private set; }
    public Guid UnpublishedLessonExerciseId { get; private set; }
    public Guid UnpublishedModuleExerciseId { get; private set; }
    public Guid InactivePathExerciseId { get; private set; }

    public const string Setup = "CREATE TABLE alunos (nome text, nota int); INSERT INTO alunos VALUES ('Bia', 8), ('Ana', 9), ('Caio', NULL);";

    /// <summary>Formato do #13 (TestCaseSpec): a tabela vem do setup do caso; o esperado fica só no banco.</summary>
    public static readonly string TestCases = $$"""
        [{ "name": "notas por nome", "setup": "{{Setup}}",
           "columns": ["nome", "nota"], "rows": [["Ana", 9], ["Bia", 8], ["Caio", null]], "orderMatters": true }]
        """;

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:DevPath"] = _postgres.GetConnectionString(),
        ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
        ["Execution:QueueKey"] = QueueKey,
        // Folgas maiores que as de produção: o Docker desta máquina é compartilhado e às vezes lento.
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

    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    public bool WorkerRunning => _worker is not null;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
            await db.Database.MigrateAsync();
            await SeedAsync(db);
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

    private async Task SeedAsync(DevPathDbContext db)
    {
        var path = CareerPath.Create($"Trilha execução {Guid.NewGuid():N}");
        var module = path.AddModule("SQL básico");
        module.Publish();
        var lesson = module.AddLesson("SELECT", ContentType.Markdown, "# SELECT");
        ExerciseId = lesson.AddExercise("Notas", "Liste nome e nota em ordem.", "sql", TestCases).Id;
        LimitedExerciseId = lesson.AddExercise("Uma tentativa", "Só uma chance.", "sql", TestCases, maxAttempts: 1).Id;
        DraftExerciseId = module.AddLesson("Em construção", ContentType.Markdown, "# ...", isDraft: true)
            .AddExercise("Rascunho", "x", "sql", TestCases).Id;
        var hidden = module.AddLesson("Escondida", ContentType.Markdown, "# ...");
        hidden.Unpublish();
        UnpublishedLessonExerciseId = hidden.AddExercise("Escondido", "x", "sql", TestCases).Id;
        UnpublishedModuleExerciseId = path.AddModule("Módulo rascunho")
            .AddLesson("Aula", ContentType.Markdown, "# ...").AddExercise("Módulo não publicado", "x", "sql", TestCases).Id;

        var inactive = CareerPath.Create($"Trilha inativa {Guid.NewGuid():N}");
        inactive.Deactivate();
        var inactiveModule = inactive.AddModule("SQL");
        inactiveModule.Publish();
        InactivePathExerciseId = inactiveModule.AddLesson("Aula", ContentType.Markdown, "# ...")
            .AddExercise("Trilha inativa", "x", "sql", TestCases).Id;

        db.CareerPaths.AddRange(path, inactive);
        await db.SaveChangesAsync();
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
