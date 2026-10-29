using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DevPath.Application.Content;

/// <summary>
/// Converte os casos de teste do conteúdo (arquivo em snake_case: <c>name</c>, <c>columns</c>, <c>rows</c>,
/// <c>order_matters</c>, <c>check_column_names</c>; <c>dataset</c> no nível do exercício) para o formato que fica
/// em <c>Exercise.TestCases</c> e que o runner lê (<c>TestCaseSpec.ParseList</c>, camelCase), combinado no #24:
/// </summary>
/// <example>
/// <code>
/// [{ "name": "...", "dataset": "livraria", "setup": null, "columns": ["titulo", "preco"],
///    "rows": [["O Hobbit", 49.90], ["Sem preço", null]], "orderMatters": false, "checkColumnNames": false }]
/// </code>
/// </example>
/// <remarks>
/// <list type="bullet">
/// <item>Todas as chaves são escritas sempre (inclusive <c>setup: null</c>), para o JSON gravado ser previsível.</item>
/// <item><c>dataset</c> do exercício é repetido em cada caso: o caso fica autossuficiente para o runner.</item>
/// <item>Números continuam números (com a escala do arquivo: 49.90), <c>null</c> continua <c>null</c>,
/// textos e true/false como estão. Nada vira texto.</item>
/// </list>
/// </remarks>
public static class ExerciseTestCaseFormat
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Mantém acentos legíveis (o jsonb do PostgreSQL guarda igual de qualquer forma).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>JSON (camelCase) dos casos de teste do exercício, pronto para <c>Exercise.TestCases</c>.</summary>
    public static string ToStoredJson(ExerciseContent exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        return ToStoredJson(exercise.TestCases, exercise.Dataset);
    }

    /// <inheritdoc cref="ToStoredJson(ExerciseContent)"/>
    /// <param name="testCases">Casos lidos do arquivo.</param>
    /// <param name="dataset">Dataset do exercício (slug), repetido em cada caso; <c>null</c> = sem dataset.</param>
    public static string ToStoredJson(IReadOnlyList<ExerciseTestCase> testCases, string? dataset)
    {
        ArgumentNullException.ThrowIfNull(testCases);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var test in testCases)
            {
                writer.WriteStartObject();
                writer.WriteString("name", test.Name);
                WriteNullableString(writer, "dataset", string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim());
                writer.WriteNull("setup"); // o conteúdo ainda não tem setup por caso
                writer.WriteStartArray("columns");
                foreach (var column in test.Columns)
                {
                    writer.WriteStringValue(column);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("rows");
                foreach (var row in test.Rows)
                {
                    writer.WriteStartArray();
                    foreach (var value in row)
                    {
                        WriteValue(writer, value, test.Name);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteBoolean("orderMatters", test.OrderMatters);
                writer.WriteBoolean("checkColumnNames", test.CheckColumnNames);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, string testName)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case decimal number:
                writer.WriteNumberValue(number); // mantém a escala: 49.90m -> 49.90
                break;
            case int or long or short or byte:
                writer.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double or float:
                writer.WriteNumberValue(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentException(
                    $"Caso de teste \"{testName}\": valor do tipo {value.GetType().Name} não é texto, número, true/false ou null.");
        }
    }
}
