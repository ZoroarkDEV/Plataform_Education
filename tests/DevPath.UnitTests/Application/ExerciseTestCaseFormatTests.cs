using System.Text.Json;
using DevPath.Application.CodeExecution;
using DevPath.Application.Content;
using FluentAssertions;

namespace DevPath.UnitTests.Application;

/// <summary>
/// Conversão dos casos de teste do conteúdo (snake_case) para o formato gravado em Exercise.TestCases (camelCase),
/// que é o que o runner do #25 lê (<see cref="TestCaseSpec.ParseList"/>).
/// </summary>
public class ExerciseTestCaseFormatTests
{
    private static readonly ExerciseTestCase Caso = new(
        "Livros de Fantasia",
        ["titulo", "preco", "disponivel"],
        [["O Hobbit", 49.90m, true], ["Sem preço", null, false]],
        OrderMatters: true,
        CheckColumnNames: true);

    [Fact]
    public void ToStoredJson_EscreveTodasAsChavesEmCamelCase()
    {
        var json = ExerciseTestCaseFormat.ToStoredJson([Caso], "livraria");

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.EnumerateArray().Single();
        item.EnumerateObject().Select(p => p.Name).Should().Equal(
            "name", "dataset", "setup", "columns", "rows", "orderMatters", "checkColumnNames");
        json.Should().NotContain("order_matters").And.NotContain("check_column_names");
        item.GetProperty("name").GetString().Should().Be("Livros de Fantasia");
        item.GetProperty("dataset").GetString().Should().Be("livraria");
        item.GetProperty("setup").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("orderMatters").GetBoolean().Should().BeTrue();
        item.GetProperty("checkColumnNames").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ToStoredJson_NumeroContinuaNumeroComAEscala_ENullContinuaNull()
    {
        var json = ExerciseTestCaseFormat.ToStoredJson([Caso], "livraria");

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement[0].GetProperty("rows");
        rows[0][0].ValueKind.Should().Be(JsonValueKind.String);
        rows[0][1].ValueKind.Should().Be(JsonValueKind.Number);
        rows[0][1].GetRawText().Should().Be("49.90", "o número não vira texto nem perde a escala");
        rows[0][2].ValueKind.Should().Be(JsonValueKind.True);
        rows[1][1].ValueKind.Should().Be(JsonValueKind.Null, "null não vira \"\" nem 0");
        rows[1][2].ValueKind.Should().Be(JsonValueKind.False);
        json.Should().Contain("\"Sem preço\"", "acentos ficam legíveis");
    }

    [Fact]
    public void ToStoredJson_FlagsAusentesNoArquivo_FicamFalse()
    {
        // O leitor do conteúdo já troca order_matters/check_column_names ausentes por false.
        var json = ExerciseTestCaseFormat.ToStoredJson([Caso with { OrderMatters = false, CheckColumnNames = false }], "livraria");

        var parsed = TestCaseSpec.ParseList(json).Single();
        parsed.OrderMatters.Should().BeFalse();
        parsed.CheckColumnNames.Should().BeFalse();
    }

    [Fact]
    public void ToStoredJson_SemDataset_GravaDatasetNull()
    {
        var json = ExerciseTestCaseFormat.ToStoredJson([Caso], dataset: null);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement[0].GetProperty("dataset").ValueKind.Should().Be(JsonValueKind.Null);
        TestCaseSpec.ParseList(json).Single().Dataset.Should().BeNull();
    }

    [Fact]
    public void ToStoredJson_ORunnerLeExatamenteOsMesmosCasos()
    {
        var segundo = new ExerciseTestCase("Contagem", ["total"], [[3m]], OrderMatters: false, CheckColumnNames: false);

        var parsed = TestCaseSpec.ParseList(ExerciseTestCaseFormat.ToStoredJson([Caso, segundo], "livraria"));

        parsed.Should().HaveCount(2);
        var first = parsed[0];
        first.Name.Should().Be("Livros de Fantasia");
        first.Dataset.Should().Be("livraria");
        first.Setup.Should().BeNull();
        first.Columns.Should().Equal("titulo", "preco", "disponivel");
        first.OrderMatters.Should().BeTrue();
        first.CheckColumnNames.Should().BeTrue();
        first.Rows[0].Should().Equal(SqlValue.Text("O Hobbit"), new SqlValue(SqlValueKind.Number, "49.90"), SqlValue.Bool(true));
        first.Rows[1].Should().Equal(SqlValue.Text("Sem preço"), SqlValue.Null, SqlValue.Bool(false));
        first.Rows[0][1].Matches(SqlValue.Number(49.9m)).Should().BeTrue("o comparador vê 49.90 == 49.9");
        parsed[1].Rows.Single().Single().Matches(SqlValue.Number(3)).Should().BeTrue();
    }

    [Fact]
    public void ToStoredJson_ListaVazia_DaArrayVazio() =>
        ExerciseTestCaseFormat.ToStoredJson([], "livraria").Should().Be("[]");

    [Fact]
    public void ToStoredJson_DoExercicio_UsaODatasetDoExercicio()
    {
        var exercise = new ExerciseContent("x.exercise.json", "T", "S", "sql", "livraria", 10, "", "SELECT 1;", [], [Caso], []);

        TestCaseSpec.ParseList(ExerciseTestCaseFormat.ToStoredJson(exercise)).Single().Dataset.Should().Be("livraria");
    }
}
