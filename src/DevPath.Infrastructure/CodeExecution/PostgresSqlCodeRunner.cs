using System.Globalization;
using System.Text;
using DevPath.Application.CodeExecution;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Roda o SQL do aluno num PostgreSQL 16 descartável, criado DENTRO do container do sandbox
/// (o mesmo banco da stack oficial, então o aluno aprende o dialeto que vai usar de verdade).
/// </summary>
/// <remarks>
/// <para>Duas camadas de proteção (detalhes e motivos no ADR-004):</para>
/// <para>
/// 1) o CONTAINER (sem rede, sem root, só leitura, limites de CPU/memória/processos) é a fronteira externa;
/// </para>
/// <para>
/// 2) dentro dele, o BANCO isola o aluno de verdade: senha (scram-sha-256) em toda conexão, inclusive
///    local; senha do superusuário aleatória por execução, que nunca chega ao processo do aluno; o aluno é
///    um papel comum (<c>aluno</c>), sem superusuário e sem os papéis que leem arquivos ou rodam programas,
///    com SELECT e nada mais; e o texto do aluno NUNCA é lido pelo psql como script (então meta-comandos
///    como <c>\!</c> ou <c>\c</c> não existem para ele). O psql lê um script FIXO, que carrega o código numa
///    variável e o manda ao servidor com <c>\gexec</c>: o servidor só entende SQL, e "\!" vira erro de sintaxe.
/// </para>
/// <para>
/// Entrada (stdin): "&lt;tamanho do código em bytes&gt;\n" + código do aluno + preparação. O tamanho na frente
/// separa as duas partes sem depender de nenhum marcador que o aluno pudesse imitar, e nada vai em argumento
/// de linha de comando (que tem limite de tamanho e aparece no <c>ps</c>).
/// </para>
/// <para>
/// Saída (stdout): cabeçalho CSV com os nomes das colunas + um JSON (<c>json_agg</c>) com os valores tipados;
/// quem lê é o <see cref="QueryResultParser"/>.
/// </para>
/// </remarks>
public sealed class PostgresSqlCodeRunner : ICodeRunner
{
    public const string Image = "postgres:16-alpine";

    /// <summary>Máximo de linhas devolvidas. O runner busca uma a mais para saber se cortou.</summary>
    public const int MaxRows = 200;

    /// <summary>Código de saída quando a PREPARAÇÃO falha: erro do exercício, não do aluno.</summary>
    public const int SetupFailedExitCode = 90;

    /// <summary>Código de saída do psql quando o SQL do aluno dá erro (ON_ERROR_STOP).</summary>
    public const int SqlErrorExitCode = 3;

    /// <summary>Tempo máximo de CADA instrução do aluno no banco (o container tem o seu próprio timeout).</summary>
    public static readonly TimeSpan DefaultStatementTimeout = TimeSpan.FromSeconds(3);

    // Script do shell DENTRO do container. $1 = statement_timeout (ms), $2 = linhas a buscar (MaxRows + 1).
    // Nenhum dado do aluno entra aqui: ele chega pelo stdin e vira arquivo em /tmp.
    public const string Script = """
        set -eu
        umask 077
        export PGDATA=/tmp/pgdata

        # 1) Entrada: primeira linha = tamanho do código do aluno em BYTES; depois o código; o resto é a preparação.
        IFS= read -r CODE_BYTES
        case "$CODE_BYTES" in ''|*[!0-9]*) echo "entrada inválida" >&2; exit 93;; esac
        dd of=/tmp/aluno.sql bs=1 count="$CODE_BYTES" 2>/dev/null
        cat > /tmp/preparo.sql

        # 2) Senhas aleatórias desta execução. A do superusuário fica só nesta variável do shell
        #    (nunca exportada) e o arquivo usado pelo initdb é apagado logo depois.
        senha() { head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n'; }
        SU_PW=$(senha)
        ALUNO_PW=$(senha)
        printf '%s\n' "$SU_PW" > /tmp/su.pw
        initdb -U postgres --auth=scram-sha-256 --pwfile=/tmp/su.pw --no-sync -E UTF8 --locale=C >/tmp/initdb.log 2>&1 || { cat /tmp/initdb.log >&2; exit 91; }
        rm -f /tmp/su.pw

        # 3) Servidor só no socket Unix (sem TCP). temp_file_limit/work_mem: uma consulta pesada não enche o /tmp.
        pg_ctl -w -s -l /tmp/pg.log -o "-c listen_addresses='' -c unix_socket_directories=/tmp -c fsync=off -c max_connections=10 -c work_mem=4MB -c temp_file_limit=16MB" start >/dev/null || { cat /tmp/pg.log >&2; exit 92; }
        su_psql() { PGPASSWORD="$SU_PW" psql -X -q -h /tmp -U postgres -v ON_ERROR_STOP=1 "$@"; }

        # 4) Papel do aluno: LOGIN com senha própria e NENHUM poder extra. Ele não é dono de nada.
        su_psql -d postgres -v pw="$ALUNO_PW" >/dev/null <<'SQL'
        CREATE ROLE aluno LOGIN PASSWORD :'pw' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
        CREATE DATABASE exercicio;
        REVOKE ALL ON DATABASE postgres FROM PUBLIC;
        REVOKE ALL ON DATABASE template1 FROM PUBLIC;
        SQL

        # 5) Preparação confiável (dataset + setup) como superusuário. Falhou = erro do exercício (exit 90).
        su_psql -d exercicio -f /tmp/preparo.sql >/dev/null 2>/tmp/preparo.err || { echo "[preparação do exercício falhou]" >&2; cat /tmp/preparo.err >&2; exit 90; }

        # 6) Permissões: só CONNECT, USAGE nos schemas do exercício e SELECT nas tabelas. Transação só leitura e
        #    statement_timeout presos ao papel. validar_consulta roda como o PRÓPRIO aluno (SECURITY INVOKER).
        su_psql -d exercicio -v timeout="$1" >/dev/null <<'SQL'
        REVOKE ALL ON DATABASE exercicio FROM PUBLIC;
        GRANT CONNECT ON DATABASE exercicio TO aluno;
        REVOKE ALL ON SCHEMA public FROM PUBLIC;
        DO $$
        DECLARE s text;
        BEGIN
          FOR s IN SELECT nspname FROM pg_namespace
                   WHERE nspname NOT LIKE 'pg\_%' AND nspname <> 'information_schema'
          LOOP
            EXECUTE format('GRANT USAGE ON SCHEMA %I TO aluno', s);
            EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO aluno', s);
          END LOOP;
        END $$;
        ALTER ROLE aluno SET default_transaction_read_only = on;
        ALTER ROLE aluno SET statement_timeout = :'timeout';
        CREATE SCHEMA devpath;
        REVOKE ALL ON SCHEMA devpath FROM PUBLIC;
        GRANT USAGE ON SCHEMA devpath TO aluno;
        CREATE FUNCTION devpath.validar_consulta(codigo text) RETURNS void
        LANGUAGE plpgsql SET search_path = public AS $f$
        DECLARE c refcursor;
        BEGIN
          OPEN c FOR EXECUTE codigo;  -- só aceita UMA consulta que devolve linhas (SELECT/WITH/VALUES/TABLE)
          CLOSE c;
        END $f$;
        REVOKE ALL ON FUNCTION devpath.validar_consulta(text) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION devpath.validar_consulta(text) TO aluno;
        SQL
        unset SU_PW

        # 7) Script FIXO do aluno. O código vira a variável :codigo (crase = conteúdo do arquivo, sem interpretar)
        #    e só vai ao servidor como VALOR: :'codigo' é um literal escapado e \gexec manda o resultado do
        #    format() direto ao servidor. O psql nunca lê o texto do aluno como comando.
        cat > /tmp/consulta.psql <<'PSQL'
        \set ON_ERROR_STOP on
        \set codigo `cat /tmp/aluno.sql`
        \o /dev/null
        SELECT devpath.validar_consulta(:'codigo');
        \o
        \pset footer off
        \pset format csv
        SELECT format(E'SELECT * FROM (\n%s\n) AS devpath_aluno LIMIT 0', :'codigo') \gexec
        \pset format unaligned
        \pset tuples_only on
        SELECT format(E'SELECT coalesce(json_agg(devpath_resultado), ''[]'') FROM (SELECT * FROM (\n%s\n) AS devpath_aluno LIMIT %s) AS devpath_resultado', :'codigo', :maxlinhas) \gexec
        PSQL
        PGPASSWORD="$ALUNO_PW" exec psql -X -q -v VERBOSITY=verbose -v maxlinhas="$2" -h /tmp -U aluno -d exercicio -f /tmp/consulta.psql </dev/null
        """;

    private readonly ICodeSandbox _sandbox;
    private readonly TimeSpan _statementTimeout;
    private readonly SandboxLimits? _limits;

    /// <param name="statementTimeout">Limite por instrução no banco; padrão <see cref="DefaultStatementTimeout"/>.</param>
    /// <param name="limits">Limites do container; nulo usa os padrões do sandbox.</param>
    public PostgresSqlCodeRunner(ICodeSandbox sandbox, TimeSpan? statementTimeout = null, SandboxLimits? limits = null)
    {
        _sandbox = sandbox;
        _statementTimeout = statementTimeout ?? DefaultStatementTimeout;
        _limits = limits;
    }

    public string Language => "sql";

    public Task<SandboxResult> RunAsync(string code, string? preparation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var query = NormalizeCode(code);
        var stdin = $"{Encoding.UTF8.GetByteCount(query)}\n{query}{preparation ?? string.Empty}";
        var request = new SandboxRequest(
            Image,
            // sh -c SCRIPT NOME $1 $2: "sh" vira o $0 do script. Só números da aplicação vão como argumento.
            ["sh", "-c", Script, "sh",
             ((long)_statementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
             (MaxRows + 1).ToString(CultureInfo.InvariantCulture)],
            Stdin: stdin,
            Limits: _limits);
        return _sandbox.ExecuteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Tira espaços e ";" do fim ("SELECT 1;" é o jeito normal de escrever, mas dentro do
    /// <c>SELECT * FROM (...)</c> o ";" seria erro de sintaxe). Caractere nulo é recusado: o psql cortaria o texto ali.
    /// </summary>
    public static string NormalizeCode(string code)
    {
        if (code.Contains('\0'))
        {
            throw new ArgumentException("O código tem um caractere inválido (nulo).", nameof(code));
        }
        return code.TrimEnd().TrimEnd(';', ' ', '\t', '\r', '\n');
    }
}
