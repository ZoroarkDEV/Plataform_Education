namespace DevPath.Application.CodeExecution;

/// <summary>Resultado da comparação. <see cref="Feedback"/> é nulo quando passou.</summary>
public sealed record ResultComparison(bool Passed, string? Feedback)
{
    public static readonly ResultComparison Pass = new(true, null);
    public static ResultComparison Fail(string feedback) => new(false, feedback);
}

/// <summary>
/// Compara o resultado do aluno com o esperado no caso de teste, com as regras do PO (issue #13):
/// <list type="number">
///   <item>a QUANTIDADE e a ORDEM das colunas sempre contam;</item>
///   <item>os NOMES das colunas só contam com <c>checkColumnNames</c> (senão "SELECT titulo AS t" também vale);</item>
///   <item>a ordem das LINHAS só conta com <c>orderMatters</c>; senão compara como multiconjunto
///         (mesmas linhas, mesma quantidade de cada, em qualquer ordem);</item>
///   <item>valores pelas regras de <see cref="SqlValue"/>: número pelo valor, null só com null, texto e bool exatos.</item>
/// </list>
/// </summary>
/// <remarks>
/// Roda no worker/aplicação, NUNCA dentro do container: o esperado não pode chegar perto do código do aluno.
/// O feedback não mostra as linhas esperadas (seria entregar a resposta), só o que está diferente.
/// </remarks>
public static class ResultComparer
{
    public static ResultComparison Compare(TestCaseSpec expected, QueryResult actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        // 1) colunas: quantidade sempre; nomes só se o caso pedir.
        if (actual.Columns.Count != expected.Columns.Count)
        {
            return ResultComparison.Fail(
                $"Sua consulta devolveu {Plural(actual.Columns.Count, "coluna", "colunas")}, mas o esperado é {Plural(expected.Columns.Count, "coluna", "colunas")}. " +
                "Confira a lista depois do SELECT.");
        }

        if (expected.CheckColumnNames)
        {
            for (var i = 0; i < expected.Columns.Count; i++)
            {
                if (!string.Equals(actual.Columns[i], expected.Columns[i], StringComparison.Ordinal))
                {
                    return ResultComparison.Fail(
                        $"A {i + 1}ª coluna deveria se chamar \"{expected.Columns[i]}\", mas se chama \"{actual.Columns[i]}\". " +
                        "Use AS para dar o nome pedido, ou confira a ordem das colunas.");
                }
            }
        }

        // 2) quantidade de linhas.
        if (actual.Truncated)
        {
            return ResultComparison.Fail(
                $"Sua consulta devolveu mais de {actual.Rows.Count} linhas, mas o esperado é {Plural(expected.Rows.Count, "linha", "linhas")}. " +
                "Faltou um filtro (WHERE) ou um JOIN está multiplicando linhas?");
        }

        if (actual.Rows.Count != expected.Rows.Count)
        {
            return ResultComparison.Fail(
                $"Sua consulta devolveu {Plural(actual.Rows.Count, "linha", "linhas")}, mas o esperado é {Plural(expected.Rows.Count, "linha", "linhas")}.");
        }

        // 3) valores. Primeiro como multiconjunto: serve para os dois modos e ajuda a explicar o erro.
        var sameMultiset = SameMultiset(expected.Rows, actual.Rows, out var unexpectedRow);
        if (!sameMultiset)
        {
            return ResultComparison.Fail(
                "Algumas linhas não batem com o resultado esperado. " +
                $"Por exemplo, esta linha da sua consulta não era esperada: ({string.Join(", ", unexpectedRow!.Select(v => v.Display))}).");
        }

        if (expected.OrderMatters)
        {
            for (var i = 0; i < expected.Rows.Count; i++)
            {
                if (RowKey(expected.Rows[i]) != RowKey(actual.Rows[i]))
                {
                    return ResultComparison.Fail(
                        $"As linhas estão certas, mas fora de ordem (a primeira diferença é na linha {i + 1}). Confira o ORDER BY.");
                }
            }
        }

        return ResultComparison.Pass;
    }

    // Multiconjunto: conta quantas vezes cada linha (pela chave canônica) aparece no esperado
    // e vai "gastando" com as linhas do aluno. Sobrou linha do aluno sem par = diferente.
    private static bool SameMultiset(
        IReadOnlyList<IReadOnlyList<SqlValue>> expected,
        IReadOnlyList<IReadOnlyList<SqlValue>> actual,
        out IReadOnlyList<SqlValue>? unexpectedRow)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in expected)
        {
            var key = RowKey(row);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        foreach (var row in actual)
        {
            var key = RowKey(row);
            if (counts.GetValueOrDefault(key) == 0)
            {
                unexpectedRow = row;
                return false;
            }
            counts[key]--;
        }

        unexpectedRow = null;
        return true;
    }

    // Junta as chaves dos valores com um separador que não aparece nelas sem escape (\u001f + tamanho),
    // para que ("a,b") e ("a", "b") nunca virem a mesma chave.
    private static string RowKey(IReadOnlyList<SqlValue> row) =>
        string.Join('\u001f', row.Select(v => $"{v.CanonicalKey.Length}:{v.CanonicalKey}"));

    private static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}
