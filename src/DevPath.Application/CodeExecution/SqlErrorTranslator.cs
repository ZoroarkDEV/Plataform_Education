using System.Text.RegularExpressions;

namespace DevPath.Application.CodeExecution;

/// <summary>Erro do PostgreSQL traduzido para quem está começando.</summary>
/// <param name="SqlState">Código padrão do erro (ex.: 42P01). Nulo se não deu para identificar.</param>
public sealed record TranslatedSqlError(string? SqlState, string FriendlyMessage, string TechnicalDetail, bool IsTimeout);

/// <summary>
/// Traduz a mensagem de erro do psql (modo VERBOSITY=verbose, que inclui o SQLSTATE) para português simples.
/// </summary>
/// <remarks>
/// O SQLSTATE é um código de 5 caracteres, igual em qualquer idioma e versão do PostgreSQL
/// (https://www.postgresql.org/docs/16/errcodes-appendix.html). Traduzir pelo código é mais confiável
/// do que procurar palavras na mensagem, que muda de versão para versão.
/// </remarks>
public static partial class SqlErrorTranslator
{
    public const string ReadOnlyMessage =
        "Neste exercício só é permitido consultar dados com SELECT. Comandos que criam, alteram ou apagam " +
        "(CREATE, DROP, INSERT, UPDATE, DELETE...) estão bloqueados.";

    public const string PermissionMessage =
        "Você não tem permissão para isso aqui. Neste exercício dá para consultar (SELECT) as tabelas do exercício, " +
        "mas não trocar de usuário, ler arquivos do servidor nem rodar programas.";

    public const string MetaCommandMessage =
        "Comandos do psql (os que começam com \\, como \\c ou \\d) não funcionam aqui. Envie só a consulta SQL.";

    public const string SingleStatementMessage =
        "Envie uma única consulta SELECT por vez (sem várias instruções separadas por ponto e vírgula).";

    public const string TimeoutMessage =
        "Sua consulta demorou demais e foi interrompida. Verifique se não falta uma condição no JOIN/WHERE " +
        "ou se há uma recursão sem fim.";

    public const string GenericMessage = "O PostgreSQL não conseguiu executar o seu SQL. Veja o detalhe técnico.";

    // "ERROR:  42P01: relation "x" does not exist" (o psql em modo verbose põe o código depois de ERROR:).
    [GeneratedRegex(@"^(?:psql:[^\n]*?:\s*)?ERROR:\s+([0-9A-Z]{5}):\s*(.*)$", RegexOptions.Multiline)]
    private static partial Regex ErrorLine();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex FirstQuoted();

    public static TranslatedSqlError Translate(string stderr)
    {
        var technical = (stderr ?? string.Empty).Trim();
        var match = ErrorLine().Match(technical);
        if (!match.Success)
        {
            return new TranslatedSqlError(null, GenericMessage, technical, IsTimeout: false);
        }

        var code = match.Groups[1].Value;
        var message = match.Groups[2].Value;
        var name = FirstQuoted().Match(message) is { Success: true } q ? q.Groups[1].Value : null;

        var friendly = code switch
        {
            // O código do aluno nunca é lido pelo psql, então "\c" ou "\!" chegam ao servidor como texto e dão erro de sintaxe.
            "42601" when name == "\\" => MetaCommandMessage,
            "42601" when name == ";" => SingleStatementMessage,
            "42601" => "Há um erro de sintaxe no seu SQL. Confira vírgulas, aspas, parênteses e as palavras-chave perto do trecho indicado.",
            // 42P11 = "cannot open ... as cursor": várias instruções, ou algo que não é consulta (COPY, ALTER, DELETE...).
            "42P11" when message.Contains("multi-query", StringComparison.Ordinal) => SingleStatementMessage,
            "42P11" => ReadOnlyMessage,
            "42P01" => name is null
                ? "Você usou uma tabela que não existe. Confira o nome das tabelas do exercício."
                : $"A tabela \"{name}\" não existe. Confira o nome das tabelas do exercício.",
            "42703" => name is null
                ? "Você usou uma coluna que não existe. Confira o nome das colunas."
                : $"A coluna \"{name}\" não existe. Confira o nome das colunas (e de qual tabela elas são).",
            "42702" => $"O nome da coluna{(name is null ? "" : $" \"{name}\"")} é ambíguo: ele existe em mais de uma tabela. Indique a tabela, ex.: alunos.nome.",
            "42803" => "Toda coluna do SELECT que não está dentro de uma função de agregação (COUNT, SUM...) precisa aparecer no GROUP BY.",
            "42883" => "Essa função ou operador não existe para esses tipos de dado. Confira o nome da função e os tipos dos valores.",
            "42804" => "Os tipos de dado não combinam (ex.: comparar texto com número).",
            "22P02" => "Um valor não pôde ser convertido para o tipo esperado (ex.: texto onde deveria haver um número).",
            "22012" => "Divisão por zero! Confira se algum divisor pode valer 0 (dica: NULLIF(divisor, 0)).",
            "21000" => "Uma subconsulta devolveu mais de uma linha onde só cabia uma.",
            "42501" => PermissionMessage,                         // insufficient_privilege
            "25006" or "25001" or "0A000" => ReadOnlyMessage,    // transação só leitura / comando não permitido
            "57014" => TimeoutMessage,                            // query_canceled: estourou o statement_timeout
            "53200" or "54001" or "53100" or "53400" => "Sua consulta usou memória ou espaço temporário demais. Tente filtrar mais os dados.",
            _ => GenericMessage
        };

        return new TranslatedSqlError(code, friendly, technical, IsTimeout: code == "57014");
    }
}
