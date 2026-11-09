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
/// Normalização: tira comentários SQL (<c>--</c> até o fim da linha e <c>/* */</c>, inclusive aninhados como no
/// PostgreSQL) que estejam FORA de texto entre aspas, corta espaços das pontas, tira <c>;</c> finais, junta qualquer
/// whitespace numa só espaço e compara sem diferenciar maiúsculas/minúsculas. Aspas simples e duplas ficam iguais
/// ao original (<c>"Fantasia"</c> ≠ <c>'Fantasia'</c>), e um <c>--</c> dentro de <c>'...'</c> ou <c>"..."</c> é
/// conteúdo, não comentário. Nunca devolve o <c>example</c> nem a solução.
/// </para>
/// <para>
/// #46: o código inicial dos exercícios vem com uma linha <c>-- ...</c>; quem a mantém precisa receber a mesma dica
/// de quem a apaga. Isto só afeta a dica: a correção compara o resultado da consulta, não este texto.
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
        var collapsed = Whitespace().Replace(StripComments(sql), " ").Trim();
        return collapsed.TrimEnd(';', ' ', '\t');
    }

    /// <summary>
    /// Troca cada comentário SQL por um espaço (ou tira o <c>--</c> até a quebra de linha), sem tocar no que está entre
    /// aspas simples (<c>''</c> é aspa escapada) ou duplas. Bloco sem fechamento vai até o fim do texto.
    /// </summary>
    public static string StripComments(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var builder = new System.Text.StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c is '\'' or '"')
            {
                // Copia o literal/identificador inteiro; aspa dobrada ('' ou "") continua dentro dele.
                var end = i + 1;
                while (end < sql.Length)
                {
                    if (sql[end] == c)
                    {
                        if (end + 1 < sql.Length && sql[end + 1] == c)
                        {
                            end += 2;
                            continue;
                        }
                        break;
                    }
                    end++;
                }
                end = Math.Min(end + 1, sql.Length);
                builder.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
                builder.Append(' ');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
                i++;
            }
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
