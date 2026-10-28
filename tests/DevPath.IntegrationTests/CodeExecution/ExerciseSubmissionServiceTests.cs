using DevPath.Application.CodeExecution;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.CodeExecution;

public sealed class SubmissionDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}

/// <summary>
/// A correção grava a CodeSubmission certa. A maioria usa um runner falso (rápido, sem sandbox);
/// um teste faz o caminho completo com o PostgreSQL de verdade dentro do sandbox.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class ExerciseSubmissionServiceTests(SubmissionDatabaseFixture fixture) : IClassFixture<SubmissionDatabaseFixture>
{
    private const string TestCases = """
        [
          { "name": "ordena por nome", "setup": "CREATE TABLE t(n text); INSERT INTO t VALUES ('Bia'),('Ana');", "expectedOutput": "n\nAna\nBia" },
          { "name": "tabela vazia", "setup": "CREATE TABLE t(n text);", "expectedOutput": "n" }
        ]
        """;

    private async Task<(Guid UserId, Guid ExerciseId)> CreateExerciseAsync(int? maxAttempts = null)
    {
        await using var db = fixture.CreateDbContext();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@devpath.test", Email = $"{Guid.NewGuid():N}@devpath.test", DisplayName = "Aluno" };
        db.Users.Add(user);
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        var exercise = path.AddModule("SQL").AddLesson("Select", ContentType.Markdown, "# x")
            .AddExercise("Ordenar", "Liste os nomes em ordem.", "sql", TestCases, maxAttempts: maxAttempts);
        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();
        return (user.Id, exercise.Id);
    }

    private async Task<SubmissionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, ICodeRunner runner)
    {
        await using var db = fixture.CreateDbContext();
        return await new ExerciseSubmissionService(db, [runner]).SubmitAsync(userId, exerciseId, code);
    }

    [Fact]
    public async Task TodosOsCasosPassam_GravaPass()
    {
        var (userId, exerciseId) = await CreateExerciseAsync();
        var runner = new FakeRunner(setup => setup!.Contains("INSERT") ? "n\r\nAna\r\nBia  \n" : "n\n");

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT n FROM t ORDER BY n;", runner);

        outcome.Result.Should().Be(SubmissionResult.Pass);
        outcome.PassedCases.Should().Be(2);
        outcome.TotalCases.Should().Be(2);

        await using var db = fixture.CreateDbContext();
        var saved = await db.CodeSubmissions.SingleAsync(s => s.Id == outcome.SubmissionId);
        saved.UserId.Should().Be(userId);
        saved.Result.Should().Be(SubmissionResult.Pass);
        saved.SubmittedCode.Should().Be("SELECT n FROM t ORDER BY n;");
        saved.Feedback.Should().Be("Todos os 2 casos de teste passaram.");
    }

    [Fact]
    public async Task SaidaDiferente_GravaFailComEsperadoERecebido()
    {
        var (userId, exerciseId) = await CreateExerciseAsync();
        var outcome = await SubmitAsync(userId, exerciseId, "SELECT n FROM t;", new FakeRunner(_ => "n\nBia\nAna\n"));

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain("ordena por nome").And.Contain("Esperado:").And.Contain("Recebido:");
    }

    [Fact]
    public async Task Timeout_ParaNoPrimeiroCaso()
    {
        var (userId, exerciseId) = await CreateExerciseAsync();
        var runner = new FakeRunner(_ => "", timedOut: true);

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT pg_sleep(60);", runner);

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain("tempo esgotado");
        runner.Calls.Should().Be(1, "não gasta outro container com código que trava");
    }

    [Fact]
    public async Task LimiteDeTentativas_EhRespeitado()
    {
        var (userId, exerciseId) = await CreateExerciseAsync(maxAttempts: 1);
        await SubmitAsync(userId, exerciseId, "SELECT 1;", new FakeRunner(_ => "x"));

        var act = () => SubmitAsync(userId, exerciseId, "SELECT 1;", new FakeRunner(_ => "x"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 tentativas*");
    }

    [Fact]
    public async Task CodigoGrandeDemais_EhRecusadoSemExecutar()
    {
        var (userId, exerciseId) = await CreateExerciseAsync();
        var runner = new FakeRunner(_ => "");

        var act = () => SubmitAsync(userId, exerciseId, new string('x', ExerciseSubmissionService.MaxCodeLength + 1), runner);

        await act.Should().ThrowAsync<ArgumentException>();
        runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExercicioInexistente_LancaKeyNotFound()
    {
        var act = () => SubmitAsync(Guid.NewGuid(), Guid.NewGuid(), "SELECT 1;", new FakeRunner(_ => ""));
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [DockerFact]
    public async Task CaminhoCompleto_ComPostgresNoSandbox()
    {
        var (userId, exerciseId) = await CreateExerciseAsync();
        var runner = new PostgresSqlCodeRunner(DockerCliSandboxTests.CreateSandbox());

        var pass = await SubmitAsync(userId, exerciseId, "SELECT n FROM t ORDER BY n;", runner);
        var fail = await SubmitAsync(userId, exerciseId, "SELECT n FROM tabela_errada;", runner);

        pass.Result.Should().Be(SubmissionResult.Pass, pass.Feedback);
        pass.ExecutionTimeMs.Should().BePositive();
        fail.Result.Should().Be(SubmissionResult.Fail);
        fail.Feedback.Should().Contain("tabela_errada");
    }

    private sealed class FakeRunner(Func<string?, string> stdout, bool timedOut = false) : ICodeRunner
    {
        public int Calls { get; private set; }
        public string Language => "sql";

        public Task<SandboxResult> RunAsync(string code, string? setup = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SandboxResult(stdout(setup), "", timedOut ? 137 : 0, TimeSpan.FromMilliseconds(5),
                timedOut, OomKilled: false, OutputTruncated: false, "fake"));
        }
    }
}
