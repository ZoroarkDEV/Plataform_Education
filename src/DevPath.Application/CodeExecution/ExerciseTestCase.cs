using System.Text.Json;

namespace DevPath.Application.CodeExecution;

/// <summary>
/// Um caso de teste de exercício, guardado no JSON <c>Exercise.TestCases</c>.
/// </summary>
/// <example>
/// <code>
/// [ { "name": "lista os nomes", "setup": "CREATE TABLE t(n text); INSERT INTO t VALUES ('Ana');",
///     "expectedOutput": "n\nAna" } ]
/// </code>
/// </example>
public sealed record ExerciseTestCase(string Name, string? Setup, string ExpectedOutput)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lê a lista de casos de teste. JSON inválido é erro de conteúdo, não do aluno.</summary>
    /// <exception cref="FormatException">JSON malformado ou caso sem <c>expectedOutput</c>.</exception>
    public static IReadOnlyList<ExerciseTestCase> ParseList(string json)
    {
        List<ExerciseTestCase>? cases;
        try
        {
            cases = JsonSerializer.Deserialize<List<ExerciseTestCase>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException("TestCases do exercício não é um JSON válido.", ex);
        }

        if (cases is null || cases.Any(c => c.ExpectedOutput is null))
        {
            throw new FormatException("Todo caso de teste precisa de 'expectedOutput'.");
        }

        return cases.Select((c, i) => c with { Name = string.IsNullOrWhiteSpace(c.Name) ? $"caso {i + 1}" : c.Name })
                    .ToList();
    }
}
