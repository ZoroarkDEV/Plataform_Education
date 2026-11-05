using System.Text.RegularExpressions;
using DevPath.Domain.CareerPaths;

namespace DevPath.Application.CodeExecution;

/// <summary>
/// Casa a tentativa do aluno com um <see cref="CommonError"/> do exercício e devolve só a mensagem amigável.
/// </summary>
/// <remarks>
/// <para>
/// Compara o TEXTO do SQL (normalizado), não o resultado de rodar o <c>example</c> de novo. Os exemplos do
/// conteúdo já são a consulta errada típica; reexecutá-los no sandbox a cada falha gastaria um container
/// por erro comum, atrasaria a resposta e ainda não cobriria erros de sintaxe (que nem chegam a ter tabela).
/// </para>
/// <para>
/// Normalização: corta espaços das pontas, tira <c>;</c> finais, junta qualquer whitespace numa só espaço
/// e compara sem diferenciar maiúsculas/minúsculas. Aspas simples e duplas ficam iguais ao original
/// (<c>"Fantasia"</c> ≠ <c>'Fantasia'</c>). Nunca devolve o <c>example</c> nem a solução.
/// </para>
/// </remarks>
public static partial class CommonErrorMatcher
{
    /// <summary>
    /// Primeiro erro comum cujo <c>example</c> casa com o código do aluno; <c>null</c> se nenhum casar
    /// ou a lista estiver vazia.
    /// </summary>
    public static string? Match(string attemptCode, IReadOnlyList<CommonError> commonErrors)
    {
        ArgumentNullException.ThrowIfNull(attemptCode);
        ArgumentNullException.ThrowIfNull(commonErrors);
        if (commonErrors.Count == 0)
        {
            return null;
        }

        var normalizedAttempt = Normalize(attemptCode);
        if (normalizedAttempt.Length == 0)
        {
            return null;
        }

        foreach (var error in commonErrors)
        {
            if (string.Equals(normalizedAttempt, Normalize(error.Example), StringComparison.OrdinalIgnoreCase))
            {
                return error.Message;
            }
        }

        return null;
    }

    /// <summary>Forma canônica do SQL para comparação (ver remarks da classe).</summary>
    public static string Normalize(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var collapsed = Whitespace().Replace(sql.Trim(), " ").Trim();
        return collapsed.TrimEnd(';', ' ', '\t');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
