using System.Globalization;
using System.Text.RegularExpressions;
using DevPath.Application.Content;
using DevPath.Infrastructure.Content;
using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Content;

/// <summary>PostgreSQL 16 descartável compartilhado pelos testes de exercícios SQL.</summary>
public sealed class SqlDatasetFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public FileSystemContentSource Source { get; } =
        new(ContentRootLocator.Resolve(null, AppContext.BaseDirectory));

    public string ConnectionString => _postgres.GetConnectionString();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

/// <summary>
/// Garante que o conteúdo SQL das aulas funciona de verdade (issue #22): para cada exercício com
/// dataset, a solução passa nos casos de teste, cada exemplo de "erro comum" NÃO passa, e todo
/// bloco ```sql das aulas roda sem erro. Comparação segue as regras do PO: resultado e não texto da
/// consulta; quantidade e ordem das colunas; nomes só se o caso pedir; ordem das linhas só se o
/// caso pedir; números pelo valor e textos exatos.
/// </summary>
public sealed partial class SqlExercisesTests(SqlDatasetFixture fixture) : IClassFixture<SqlDatasetFixture>
{
    // Blocos que começam assim são modelos de sintaxe (ex.: SELECT colunas FROM tabela), não consultas.
    private const string TemplateMarker = "-- Formato geral";

    public static TheoryData<string> SqlExercises()
    {
        var data = new TheoryData<string>();
        foreach (var lesson in LoadLessons().Where(l => l.Exercise is { Language: "sql", Dataset: not null }))
        {
            data.Add(lesson.SourceFile);
        }

        return data;
    }

    [Fact]
    public void ExisteAoMenosUmExercicioSqlParaTestar()
    {
        SqlExercises().Should().HaveCountGreaterThanOrEqualTo(5);
    }

    [Theory]
    [MemberData(nameof(SqlExercises))]
    public async Task Solucao_PassaEmTodosOsCasosDeTeste(string lessonFile)
    {
        var exercise = Lesson(lessonFile).Exercise!;
        await LoadDatasetAsync(exercise.Dataset!);

        var result = await RunAsync(exercise.Solution);

        result.Error.Should().BeNull($"a solução de {exercise.SourceFile} deveria rodar");
        foreach (var test in exercise.TestCases)
        {
            Mismatch(test, result).Should().BeNull($"a solução de {exercise.SourceFile} deveria passar em '{test.Name}'");
        }
    }

    [Theory]
    [MemberData(nameof(SqlExercises))]
    public async Task ErrosComuns_NaoPassamNosCasosDeTeste(string lessonFile)
    {
        var exercise = Lesson(lessonFile).Exercise!;
        await LoadDatasetAsync(exercise.Dataset!);

        foreach (var commonError in exercise.CommonErrors)
        {
            var result = await RunAsync(commonError.Example);
            var passes = result.Error is null && exercise.TestCases.All(t => Mismatch(t, result) is null);
            passes.Should().BeFalse($"o erro comum '{commonError.Example}' de {exercise.SourceFile} não pode ser aceito como resposta certa");
        }
    }

    [Theory]
    [MemberData(nameof(SqlExercises))]
    public async Task ExemplosDaAula_RodamSemErro(string lessonFile)
    {
        var lesson = Lesson(lessonFile);
        await LoadDatasetAsync(lesson.Exercise!.Dataset!);

        var blocks = SqlBlock().Matches(lesson.Body).Select(m => m.Groups["sql"].Value)
            .Where(b => !b.TrimStart().StartsWith(TemplateMarker, StringComparison.Ordinal))
            .ToList();

        blocks.Should().NotBeEmpty($"{lesson.SourceFile} deveria ter exemplos em blocos ```sql");
        foreach (var block in blocks)
        {
            (await RunAsync(block)).Error.Should().BeNull($"o exemplo de {lesson.SourceFile} deveria rodar:\n{block}");
        }
    }

    [Fact]
    public void Comparacao_NumerosPeloValorEOrdemSoQuandoPedida()
    {
        var test = new ExerciseTestCase("caso", ["titulo", "preco"], [["A", 1.50m], ["B", 2m]], OrderMatters: false, CheckColumnNames: false);
        var reversed = new QueryResult(["t", "p"], [["B", 2.00m], ["A", 1.5m]], null);

        Mismatch(test, reversed).Should().BeNull();
        Mismatch(test with { OrderMatters = true }, reversed).Should().NotBeNull();
        Mismatch(test with { CheckColumnNames = true }, reversed).Should().NotBeNull();
        Mismatch(test, new QueryResult(["t"], [["B"], ["A"]], null)).Should().NotBeNull();
        Mismatch(test, new QueryResult(["t", "p"], [["a", 1.5m], ["B", 2m]], null)).Should().NotBeNull();
    }

    private static IReadOnlyList<LessonContent> LoadLessons() =>
        new FileSystemContentSource(ContentRootLocator.Resolve(null, AppContext.BaseDirectory)).LoadPaths()
            .SelectMany(p => p.Modules).SelectMany(m => m.Lessons).ToList();

    private static LessonContent Lesson(string sourceFile) => LoadLessons().Single(l => l.SourceFile == sourceFile);

    private async Task LoadDatasetAsync(string dataset)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(fixture.Source.DatasetPath(dataset)), connection);
        await command.ExecuteNonQueryAsync();
    }

    // Roda em transação somente leitura e desfaz no fim: o aluno (e o exemplo) não alteram o dataset.
    private async Task<QueryResult> RunAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync();
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<IReadOnlyList<object?>>();
            while (await reader.ReadAsync())
            {
                rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => Normalize(reader.GetValue(i))).ToList());
            }

            return new QueryResult(columns, rows, null);
        }
        catch (PostgresException ex)
        {
            return new QueryResult([], [], ex.MessageText);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static string? Mismatch(ExerciseTestCase test, QueryResult result)
    {
        if (result.Error is not null)
        {
            return $"erro: {result.Error}";
        }

        if (result.Columns.Count != test.Columns.Count)
        {
            return $"esperava {test.Columns.Count} colunas, veio {result.Columns.Count}";
        }

        if (test.CheckColumnNames && !result.Columns.SequenceEqual(test.Columns))
        {
            return $"nomes de coluna: esperava {string.Join(", ", test.Columns)}, veio {string.Join(", ", result.Columns)}";
        }

        var expected = test.Rows.Select(RowKey).ToList();
        var actual = result.Rows.Select(RowKey).ToList();
        if (!test.OrderMatters)
        {
            expected.Sort(StringComparer.Ordinal);
            actual.Sort(StringComparer.Ordinal);
        }

        return expected.SequenceEqual(actual) ? null : $"linhas diferentes:\n{string.Join("\n", actual)}";
    }

    private static string RowKey(IReadOnlyList<object?> row) => string.Join(" | ", row.Select(v => Normalize(v) switch
    {
        null => "NULL",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        var other => "'" + other + "'"
    }));

    // Números viram decimal sem zeros à direita (29.90 == 29.9; COUNT bigint 4 == 4).
    private static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        short or int or long or decimal or float or double => Convert.ToDecimal(value, CultureInfo.InvariantCulture) / 1.0000000000000000000000000000m,
        bool or string => value,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, string? Error);

    [GeneratedRegex(@"```sql\n(?<sql>.*?)```", RegexOptions.Singleline)]
    private static partial Regex SqlBlock();
}
