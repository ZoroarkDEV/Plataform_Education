using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Content;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.CodeExecution;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Content;

/// <summary>Banco descartável com o /content real já semeado (uma vez para a classe toda).</summary>
public sealed class SeededContentFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var source = new FileSystemContentSource(ContentRootLocator.Resolve(null, AppContext.BaseDirectory));
        await new ContentSeeder(db, source, NullLogger<ContentSeeder>.Instance).SeedAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(ConnectionString).Options);
}

/// <summary>
/// Ponta a ponta do #24: o que o seed grava em Exercise.TestCases e na tabela Datasets é exatamente o que o
/// runner do #25 consome. Para cada exercício SQL semeado: o <see cref="TestCaseSpec.ParseList"/> do runner lê os
/// casos, eles batem com o arquivo, e a SOLUÇÃO passa no <see cref="ResultComparer"/> rodando contra o dataset
/// carregado do banco. O teste [DockerFact] faz o mesmo pelo serviço de correção com o sandbox de verdade.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class SeededExercisesEndToEndTests(SeededContentFixture fixture) : IClassFixture<SeededContentFixture>
{
    private static readonly IReadOnlyList<Application.Content.LessonContent> ContentLessons =
        new FileSystemContentSource(ContentRootLocator.Resolve(null, AppContext.BaseDirectory)).LoadPaths()
            .SelectMany(p => p.Modules).SelectMany(m => m.Lessons).ToList();

    public static TheoryData<string> SqlLessonSlugs()
    {
        var data = new TheoryData<string>();
        foreach (var lesson in ContentLessons.Where(l => l.Exercise is { Language: "sql", Dataset: not null }))
        {
            data.Add(lesson.Slug);
        }

        return data;
    }

    [Fact]
    public void ModuloSqlTemOs5Exercicios() => SqlLessonSlugs().Should().HaveCount(5);

    [Theory]
    [MemberData(nameof(SqlLessonSlugs))]
    public async Task CasosGravados_ORunnerLeOsMesmosCasosDoArquivo(string lessonSlug)
    {
        var content = ContentLessons.Single(l => l.Slug == lessonSlug).Exercise!;
        var exercise = await ExerciseAsync(lessonSlug);

        var cases = TestCaseSpec.ParseList(exercise.TestCases); // lança se o formato não for o do runner

        cases.Should().HaveCount(content.TestCases.Count);
        foreach (var (spec, file) in cases.Zip(content.TestCases))
        {
            spec.Name.Should().Be(file.Name);
            spec.Dataset.Should().Be(content.Dataset);
            spec.Setup.Should().BeNull();
            spec.Columns.Should().Equal(file.Columns);
            spec.OrderMatters.Should().Be(file.OrderMatters);
            spec.CheckColumnNames.Should().Be(file.CheckColumnNames);
            spec.Rows.Should().HaveCount(file.Rows.Count);
            foreach (var (row, fileRow) in spec.Rows.Zip(file.Rows))
            {
                row.Select(v => v.CanonicalKey).Should().Equal(fileRow.Select(ToSqlValue).Select(v => v.CanonicalKey),
                    "número continua número (pelo valor), null continua null e texto continua texto");
            }
        }

        exercise.CommonErrors.Select(e => e.Example).Should().Equal(content.CommonErrors.Select(e => e.Example));
        exercise.MaxAttempts.Should().BeNull();
        exercise.Points.Should().Be(10);
        exercise.IsPublished.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(SqlLessonSlugs))]
    public async Task Solucao_PassaNoComparadorDoRunner_ComODatasetDoBanco(string lessonSlug)
    {
        var exercise = await ExerciseAsync(lessonSlug);
        var cases = TestCaseSpec.ParseList(exercise.TestCases);

        foreach (var testCase in cases)
        {
            var actual = await RunLikeTheRunnerAsync(testCase.Dataset ?? exercise.Dataset, testCase.Setup, exercise.SolutionCode);

            var comparison = ResultComparer.Compare(testCase, actual);

            comparison.Passed.Should().BeTrue($"a solução de '{lessonSlug}' deveria passar em '{testCase.Name}': {comparison.Feedback}");
        }
    }

    [Theory]
    [MemberData(nameof(SqlLessonSlugs))]
    public async Task ErrosComunsGravados_NaoPassamNoComparadorDoRunner(string lessonSlug)
    {
        var exercise = await ExerciseAsync(lessonSlug);
        var cases = TestCaseSpec.ParseList(exercise.TestCases);

        exercise.CommonErrors.Should().NotBeEmpty();
        foreach (var commonError in exercise.CommonErrors)
        {
            var passesAll = true;
            foreach (var testCase in cases)
            {
                QueryResult actual;
                try
                {
                    actual = await RunLikeTheRunnerAsync(testCase.Dataset ?? exercise.Dataset, testCase.Setup, commonError.Example);
                }
                catch (PostgresException)
                {
                    passesAll = false; // erro de SQL: o aluno recebe erro, não "passou"
                    break;
                }

                passesAll &= ResultComparer.Compare(testCase, actual).Passed;
            }

            passesAll.Should().BeFalse($"o erro comum '{commonError.Example}' de '{lessonSlug}' não pode passar");
        }
    }

    [DockerFact]
    public async Task CaminhoCompleto_SolucaoDeCadaExercicioSemeado_PassaNoSandbox()
    {
        foreach (var lessonSlug in ContentLessons.Where(l => l.Exercise is { Language: "sql", Dataset: not null }).Select(l => l.Slug))
        {
            var exercise = await ExerciseAsync(lessonSlug);
            await using var db = fixture.CreateDbContext();
            var user = new DevPath.Infrastructure.Identity.ApplicationUser
            {
                UserName = $"{Guid.NewGuid():N}@devpath.test", Email = $"{Guid.NewGuid():N}@devpath.test", DisplayName = "Aluno"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var runner = new PostgresSqlCodeRunner(DockerCliSandboxTests.CreateSandbox(),
                limits: new SandboxLimits { Timeout = TimeSpan.FromSeconds(60) });
            var outcome = await new ExerciseSubmissionService(db, new DatasetRepository(db), [runner])
                .SubmitAsync(user.Id, exercise.Id, exercise.SolutionCode);

            outcome.Status.Should().Be(ExecutionStatus.Pass,
                $"{lessonSlug}: {outcome.FriendlyMessage} {outcome.TechnicalDetail} "
                + string.Join("; ", outcome.TestCases.Select(t => $"{t.Name}: {t.Message}")));
        }
    }

    private async Task<Domain.CareerPaths.Exercise> ExerciseAsync(string lessonSlug)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Exercises.AsNoTracking()
            .SingleAsync(e => db.Lessons.Any(l => l.Id == e.LessonId && l.Slug == lessonSlug));
    }

    // O mesmo caminho do runner, sem o container: banco novo, dataset (da tabela Datasets) + setup, e a consulta
    // embrulhada como no PostgresSqlCodeRunner (LIMIT 0 para o cabeçalho, json_agg para os valores). A saída
    // passa pelo QueryResultParser do runner.
    private async Task<QueryResult> RunLikeTheRunnerAsync(string? datasetSlug, string? setup, string code)
    {
        string? datasetSql = null;
        if (datasetSlug is not null)
        {
            await using var db = fixture.CreateDbContext();
            datasetSql = (await new DatasetRepository(db).FindBySlugAsync(datasetSlug)
                ?? throw new DatasetNotFoundException(datasetSlug)).Sql;
        }

        var database = $"exercicio_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var preparation = ExerciseSubmissionService.BuildPreparation(datasetSql, setup);
        if (preparation is not null)
        {
            await using var prepare = new NpgsqlCommand(preparation, connection);
            await prepare.ExecuteNonQueryAsync();
        }

        var query = PostgresSqlCodeRunner.NormalizeCode(code);
        var columns = new List<string>();
        await using (var header = new NpgsqlCommand($"SELECT * FROM (\n{query}\n) AS devpath_aluno LIMIT 0", connection))
        await using (var reader = await header.ExecuteReaderAsync())
        {
            columns.AddRange(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
        }

        await using var values = new NpgsqlCommand(
            "SELECT coalesce(json_agg(devpath_resultado), '[]')::text FROM (SELECT * FROM (\n" + query
            + $"\n) AS devpath_aluno LIMIT {PostgresSqlCodeRunner.MaxRows + 1}) AS devpath_resultado", connection);
        var json = (string)(await values.ExecuteScalarAsync())!;

        return QueryResultParser.Parse(string.Join(',', columns) + "\n" + json + "\n", PostgresSqlCodeRunner.MaxRows);
    }

    private static SqlValue ToSqlValue(object? value) => value switch
    {
        null => SqlValue.Null,
        string text => SqlValue.Text(text),
        bool flag => SqlValue.Bool(flag),
        decimal number => SqlValue.Number(number),
        _ => throw new InvalidOperationException($"valor inesperado no conteúdo: {value}")
    };
}
