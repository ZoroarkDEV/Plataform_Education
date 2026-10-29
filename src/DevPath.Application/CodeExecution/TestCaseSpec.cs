using System.Text.Json;

namespace DevPath.Application.CodeExecution;

/// <summary>
/// Um caso de teste de exercício, do jeito que fica gravado em <c>Exercise.TestCases</c> (jsonb, camelCase).
/// Formato combinado com Dev/PO na issue #13:
/// </summary>
/// <example>
/// <code>
/// [ {
///   "name": "lista título e preço",
///   "dataset": "livraria",            // opcional: slug da tabela Datasets
///   "setup": "UPDATE livros ...",     // opcional: SQL extra, roda depois do dataset
///   "columns": ["titulo", "preco"],
///   "rows": [["Dom Casmurro", 29.9], ["O Cortiço", null]],
///   "orderMatters": false,            // padrão false: compara as linhas como multiconjunto
///   "checkColumnNames": false         // padrão false: só a quantidade e a ordem das colunas contam
/// } ]
/// </code>
/// </example>
/// <remarks>
/// O resultado esperado (<see cref="Columns"/>/<see cref="Rows"/>) NUNCA vai para o container: só o dataset
/// e o setup entram lá. A comparação acontece fora, no <see cref="ResultComparer"/> (ADR-004).
/// </remarks>
public sealed record TestCaseSpec(
    string Name,
    string? Dataset,
    string? Setup,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<SqlValue>> Rows,
    bool OrderMatters,
    bool CheckColumnNames)
{
    // Web = camelCase e sem diferenciar maiúsculas; recusa chaves desconhecidas para pegar erro de digitação.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Lê e valida a lista. Problema aqui é erro de conteúdo, não do aluno.</summary>
    /// <exception cref="FormatException">JSON malformado ou caso inválido (mensagem diz qual e por quê).</exception>
    public static IReadOnlyList<TestCaseSpec> ParseList(string json)
    {
        List<Entry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<Entry>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"TestCases do exercício não está no formato combinado: {ex.Message}", ex);
        }

        if (entries is null || entries.Count == 0)
        {
            throw new FormatException("O exercício precisa ter pelo menos um caso de teste.");
        }

        return entries.Select(Validate).ToList();
    }

    private static TestCaseSpec Validate(Entry entry, int index)
    {
        var name = string.IsNullOrWhiteSpace(entry.Name) ? $"caso {index + 1}" : entry.Name.Trim();
        var where = $"Caso de teste \"{name}\"";

        if (entry.Columns is null || entry.Rows is null)
        {
            throw new FormatException($"{where}: 'columns' e 'rows' são obrigatórios.");
        }

        var rows = new List<IReadOnlyList<SqlValue>>();
        foreach (var (row, i) in entry.Rows.Select((r, i) => (r, i)))
        {
            if (row is null || row.Count != entry.Columns.Count)
            {
                throw new FormatException($"{where}: a linha {i + 1} tem {row?.Count ?? 0} valores, mas há {entry.Columns.Count} colunas.");
            }

            var values = row.Select(SqlValue.FromJson).ToList();
            if (values.Any(v => v.Kind == SqlValueKind.Other))
            {
                throw new FormatException($"{where}: a linha {i + 1} tem um valor que não é texto, número, true/false ou null.");
            }
            rows.Add(values);
        }

        return new TestCaseSpec(
            name,
            string.IsNullOrWhiteSpace(entry.Dataset) ? null : entry.Dataset.Trim(),
            string.IsNullOrWhiteSpace(entry.Setup) ? null : entry.Setup,
            entry.Columns.Select(c => c ?? string.Empty).ToList(),
            rows,
            entry.OrderMatters ?? false,
            entry.CheckColumnNames ?? false);
    }

    private sealed class Entry
    {
        public string? Name { get; set; }
        public string? Dataset { get; set; }
        public string? Setup { get; set; }
        public List<string?>? Columns { get; set; }
        public List<List<JsonElement>?>? Rows { get; set; }
        public bool? OrderMatters { get; set; }
        public bool? CheckColumnNames { get; set; }
    }
}
