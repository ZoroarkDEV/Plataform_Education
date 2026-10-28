using DevPath.Application.CodeExecution;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Datasets;
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
/// os testes [DockerFact] fazem o caminho completo: dataset do banco, PostgreSQL de verdade no sandbox,
/// comparação estruturada fora do container.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class ExerciseSubmissionServiceTests(SubmissionDatabaseFixture fixture) : IClassFixture<SubmissionDatabaseFixture>
{
    // Dataset pequeno, no mesmo estilo de content/datasets/livraria.sql.
    private const string MiniDatasetSql = """
        DROP TABLE IF EXISTS livros;
        CREATE TABLE livros (id int PRIMARY KEY, titulo text NOT NULL, preco numeric(10,2), disponivel boolean);
        INSERT INTO livros VALUES (1, 'Dom Casmurro', 29.90, true), (2, 'O Cortiço', 35.00, false), (3, 'Iracema', NULL, true);
        """;

    private static string TestCases(string dataset) => $$"""
        [
          { "name": "livros caros", "dataset": "{{dataset}}",
            "columns": ["titulo", "preco"], "rows": [["O Cortiço", 35], ["Dom Casmurro", 29.9]],
            "orderMatters": true },
          { "name": "com setup", "dataset": "{{dataset}}", "setup": "UPDATE livros SET preco = 99 WHERE id = 1;",
            "columns": ["titulo", "preco"], "rows": [["Dom Casmurro", 99.00], ["O Cortiço", 35.0]],
            "orderMatters": true }
        ]
        """;

    private async Task<string> CreateDatasetAsync()
    {
        await using var db = fixture.CreateDbContext();
        var dataset = Dataset.Create($"mini-{Guid.NewGuid():N}", MiniDatasetSql);
        db.Datasets.Add(dataset);
        await db.SaveChangesAsync();
        return dataset.Slug;
    }

    private async Task<(Guid UserId, Guid ExerciseId)> CreateExerciseAsync(string testCases, int? maxAttempts = null)
    {
        await using var db = fixture.CreateDbContext();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@devpath.test", Email = $"{Guid.NewGuid():N}@devpath.test", DisplayName = "Aluno" };
        db.Users.Add(user);
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        var exercise = path.AddModule("SQL").AddLesson("Select", ContentType.Markdown, "# x")
            .AddExercise("Caros", "Liste os livros com preço, do mais caro para o mais barato.", "sql", testCases, maxAttempts: maxAttempts);
        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();
        return (user.Id, exercise.Id);
    }

    private async Task<SubmissionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, ICodeRunner runner)
    {
        await using var db = fixture.CreateDbContext();
        return await new ExerciseSubmissionService(db, new DatasetRepository(db), [runner]).SubmitAsync(userId, exerciseId, code);
    }

    private static PostgresSqlCodeRunner RealRunner() =>
        new(DockerCliSandboxTests.CreateSandbox(), limits: new SandboxLimits { Timeout = TimeSpan.FromSeconds(30) });

    // Saída no formato do runner de verdade: cabeçalho CSV + json_agg.
    private const string CorrectStdout = "titulo,preco\n[{\"titulo\":\"O Cortiço\",\"preco\":35.00},{\"titulo\":\"Dom Casmurro\",\"preco\":29.90}]\n";

    [Fact]
    public async Task ResultadoCerto_GravaPass_EPreparacaoTemDatasetESetup_MasNuncaOEsperado()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));
        var runner = new FakeRunner(prep => prep!.Contains("UPDATE")
            ? "titulo,preco\n[{\"titulo\":\"Dom Casmurro\",\"preco\":99.00},{\"titulo\":\"O Cortiço\",\"preco\":35.00}]\n"
            : CorrectStdout);

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT titulo, preco FROM livros ORDER BY preco DESC;", runner);

        outcome.Result.Should().Be(SubmissionResult.Pass, outcome.Feedback);
        outcome.PassedCases.Should().Be(2);
        outcome.TotalCases.Should().Be(2);
        runner.Preparations.Should().HaveCount(2);
        runner.Preparations[0].Should().StartWith(MiniDatasetSql).And.NotContain("UPDATE");
        runner.Preparations[1].Should().StartWith(MiniDatasetSql).And.EndWith("UPDATE livros SET preco = 99 WHERE id = 1;\n");
        // Invariante do ADR-004: o resultado esperado nunca entra no container.
        runner.Preparations.Should().AllSatisfy(p => p.Should().NotContain("\"columns\"").And.NotContain("orderMatters"));

        await using var db = fixture.CreateDbContext();
        var saved = await db.CodeSubmissions.SingleAsync(s => s.Id == outcome.SubmissionId);
        saved.UserId.Should().Be(userId);
        saved.Result.Should().Be(SubmissionResult.Pass);
        saved.Feedback.Should().Be("Todos os 2 casos de teste passaram.");
    }

    [Fact]
    public async Task OrdemErrada_GravaFailComDica()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));
        var reversed = "titulo,preco\n[{\"titulo\":\"Dom Casmurro\",\"preco\":29.90},{\"titulo\":\"O Cortiço\",\"preco\":35.00}]\n";

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT titulo, preco FROM livros;", new FakeRunner(_ => reversed));

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain("livros caros").And.Contain("ORDER BY");
    }

    [Fact]
    public async Task ErroDoBanco_ViraMensagemAmigavelComDetalhe()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));
        var runner = new FakeRunner(_ => "", exitCode: 3,
            stderr: "psql:/tmp/consulta.psql:4: ERROR:  42P01: relation \"livro\" does not exist\nLINE 1: SELECT * FROM livro");

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT * FROM livro", runner);

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain("A tabela \"livro\" não existe").And.Contain("Detalhe técnico")
            .And.NotContain("/tmp/consulta.psql");
    }

    [Fact]
    public async Task Timeout_ParaNoPrimeiroCaso()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));
        var runner = new FakeRunner(_ => "", timedOut: true);

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT pg_sleep(60);", runner);

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain("demorou demais");
        runner.Calls.Should().Be(1, "não gasta outro container com código que trava");
    }

    [Fact]
    public async Task DatasetInexistente_ErroClaro_SemExecutarNemGravar()
    {
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases("nao-existe"));
        var runner = new FakeRunner(_ => CorrectStdout);

        var act = () => SubmitAsync(userId, exerciseId, "SELECT 1", runner);

        (await act.Should().ThrowAsync<DatasetNotFoundException>()).Which.Message.Should().Contain("nao-existe");
        runner.Calls.Should().Be(0);
        await using var db = fixture.CreateDbContext();
        (await db.CodeSubmissions.CountAsync(s => s.ExerciseId == exerciseId)).Should().Be(0);
    }

    [Fact]
    public async Task LimiteDeTentativas_EhRespeitado()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug), maxAttempts: 1);
        await SubmitAsync(userId, exerciseId, "SELECT 1;", new FakeRunner(_ => CorrectStdout));

        var act = () => SubmitAsync(userId, exerciseId, "SELECT 1;", new FakeRunner(_ => CorrectStdout));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 tentativas*");
    }

    [Theory]
    [InlineData(ExerciseSubmissionService.MaxCodeLength + 1)]
    [InlineData(0)]
    public async Task CodigoGrandeDemaisOuVazio_EhRecusadoSemExecutar(int length)
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));
        var runner = new FakeRunner(_ => "");

        var act = () => SubmitAsync(userId, exerciseId, new string(' ', Math.Max(0, length - 1)) + (length > 0 ? "x" : ""), runner);

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
    public async Task CaminhoCompleto_ComDatasetEPostgresNoSandbox()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));

        // Nomes diferentes (sem checkColumnNames) e 29.90 vs 29.9 continuam valendo.
        var pass = await SubmitAsync(userId, exerciseId,
            "SELECT titulo AS t, preco AS p FROM livros WHERE preco IS NOT NULL ORDER BY preco DESC;", RealRunner());
        var wrongOrder = await SubmitAsync(userId, exerciseId,
            "SELECT titulo, preco FROM livros WHERE preco IS NOT NULL ORDER BY preco;", RealRunner());
        var error = await SubmitAsync(userId, exerciseId, "SELECT titulo FROM tabela_errada", RealRunner());

        pass.Result.Should().Be(SubmissionResult.Pass, pass.Feedback);
        pass.ExecutionTimeMs.Should().BePositive();
        wrongOrder.Result.Should().Be(SubmissionResult.Fail);
        wrongOrder.Feedback.Should().Contain("fora de ordem");
        error.Result.Should().Be(SubmissionResult.Fail);
        error.Feedback.Should().Contain("A tabela \"tabela_errada\" não existe");
    }

    [DockerFact]
    public async Task CaminhoCompleto_MetaComandoNaoEscapa()
    {
        var slug = await CreateDatasetAsync();
        var (userId, exerciseId) = await CreateExerciseAsync(TestCases(slug));

        var outcome = await SubmitAsync(userId, exerciseId, "SELECT 1; \\! id", RealRunner());

        outcome.Result.Should().Be(SubmissionResult.Fail);
        outcome.Feedback.Should().Contain(SqlErrorTranslator.MetaCommandMessage).And.NotContain("uid=");
    }

    private sealed class FakeRunner(Func<string?, string> stdout, bool timedOut = false, int exitCode = 0, string stderr = "") : ICodeRunner
    {
        public int Calls { get; private set; }
        public List<string?> Preparations { get; } = [];
        public string Language => "sql";

        public Task<SandboxResult> RunAsync(string code, string? preparation = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Preparations.Add(preparation);
            return Task.FromResult(new SandboxResult(stdout(preparation), stderr, timedOut ? 137 : exitCode, TimeSpan.FromMilliseconds(5),
                timedOut, OomKilled: false, OutputTruncated: false, "fake"));
        }
    }
}
