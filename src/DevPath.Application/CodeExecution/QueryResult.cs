using System.Text;
using System.Text.Json;

namespace DevPath.Application.CodeExecution;

/// <summary>Resultado estruturado da consulta do aluno: nomes das colunas e valores com tipo.</summary>
/// <param name="Truncated">Havia mais linhas do que o limite; só as primeiras vieram.</param>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<SqlValue>> Rows,
    bool Truncated);

/// <summary>
/// Lê a saída do runner de SQL. Ela tem duas partes, sempre nesta ordem (ver ADR-004):
/// 1) o CABEÇALHO em CSV: só os nomes das colunas, na ordem (vem de um <c>LIMIT 0</c>, então
///    funciona até quando o resultado é vazio e preserva nomes repetidos);
/// 2) os VALORES num único JSON: <c>json_agg</c> das linhas, ex.: <c>[{"titulo":"Dom Casmurro","preco":29.90}]</c>.
/// </summary>
/// <remarks>
/// Por que JSON e não texto? O JSON do PostgreSQL já diz o TIPO de cada valor: número sem aspas,
/// texto com aspas, <c>true</c>/<c>false</c> e <c>null</c> (que em texto puro seria igual a uma string vazia).
/// Com isso o comparador aplica as regras do PO (29.9 == 29.90, null só com null) sem adivinhar.
/// O tipo <c>json</c> (e não <c>jsonb</c>) mantém a ordem das colunas e nomes repetidos.
/// </remarks>
public static class QueryResultParser
{
    /// <summary>Converte a saída; <paramref name="maxRows"/> linhas no máximo (o runner busca uma a mais para saber se cortou).</summary>
    /// <exception cref="FormatException">A saída não está no formato esperado (bug do runner, não do aluno).</exception>
    public static QueryResult Parse(string stdout, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var (columns, rest) = ReadCsvRecord(stdout);

        List<IReadOnlyList<SqlValue>> rows = [];
        try
        {
            using var json = JsonDocument.Parse(rest);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Os valores do resultado deveriam ser um array JSON.");
            }

            foreach (var row in json.RootElement.EnumerateArray())
            {
                // EnumerateObject devolve as propriedades na ordem do texto, inclusive nomes repetidos.
                var values = row.EnumerateObject().Select(p => SqlValue.FromJson(p.Value)).ToList();
                if (values.Count != columns.Count)
                {
                    throw new FormatException($"Linha com {values.Count} valores, mas o cabeçalho tem {columns.Count} colunas.");
                }
                rows.Add(values);
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException("Os valores do resultado não são um JSON válido.", ex);
        }

        var truncated = rows.Count > maxRows;
        return new QueryResult(columns, truncated ? rows[..maxRows] : rows, truncated);
    }

    // Lê UM registro CSV (RFC 4180) do começo do texto: campos separados por vírgula, aspas duplas quando
    // o nome tem vírgula, aspas ou quebra de linha (aspas dentro viram ""). Devolve o resto do texto.
    private static (IReadOnlyList<string> Fields, string Remainder) ReadCsvRecord(string text)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (c == '\n')
            {
                break;
            }
            else if (c != '\r')
            {
                current.Append(c);
            }
        }

        if (i >= text.Length)
        {
            throw new FormatException("A saída não tem o cabeçalho seguido dos valores.");
        }

        // Linha vazia = consulta sem nenhuma coluna (ex.: SELECT FROM t). Senão, o último campo também conta.
        if (fields.Count > 0 || current.Length > 0)
        {
            fields.Add(current.ToString());
        }
        return (fields, text[(i + 1)..]);
    }
}
