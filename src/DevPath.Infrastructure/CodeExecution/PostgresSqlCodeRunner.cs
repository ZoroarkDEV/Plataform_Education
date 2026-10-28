using DevPath.Application.CodeExecution;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Roda o SQL do aluno num PostgreSQL 16 descartável, criado DENTRO do container do sandbox
/// (o mesmo banco da stack oficial, então o aluno aprende o dialeto que vai usar de verdade).
/// </summary>
/// <remarks>
/// Passo a passo dentro do container (≈1,5 s no total):
/// 1) <c>initdb</c> cria um cluster vazio em /tmp (o único lugar gravável, em memória);
/// 2) <c>pg_ctl</c> sobe o servidor ouvindo só num socket Unix (sem TCP, e a rede já é "none");
/// 3) cria o papel <c>aluno</c>, SEM superusuário, dono do banco <c>exercicio</c>;
/// 4) roda a preparação do caso de teste (CREATE TABLE, INSERT...) como <c>aluno</c>;
/// 5) roda o código do aluno, lido do stdin, como <c>aluno</c>.
/// Como <c>aluno</c> não é superusuário, comandos perigosos como <c>COPY ... TO PROGRAM</c> são negados.
/// A saída usa o formato "sem alinhamento" do psql: cabeçalho e linhas separados por "|", sem rodapé.
/// </remarks>
public sealed class PostgresSqlCodeRunner(ICodeSandbox sandbox) : ICodeRunner
{
    public const string Image = "postgres:16-alpine";

    /// <summary>Código de saída quando a PREPARAÇÃO falha: erro do exercício, não do aluno.</summary>
    public const int SetupFailedExitCode = 90;

    /// <summary>Código de saída do psql quando o script do aluno dá erro (ON_ERROR_STOP).</summary>
    public const int SqlErrorExitCode = 3;

    // Script do shell DENTRO do container. "$1" é a preparação, passada como argumento (sem montar
    // string de comando), então aspas no SQL não quebram nada.
    private const string Script = """
        set -eu
        export PGDATA=/tmp/pgdata
        initdb -U postgres --auth=trust --no-sync -E UTF8 --locale=C >/tmp/initdb.log 2>&1 || { cat /tmp/initdb.log >&2; exit 91; }
        pg_ctl -w -s -l /tmp/pg.log -o "-c listen_addresses='' -c unix_socket_directories=/tmp -c fsync=off" start >/dev/null || { cat /tmp/pg.log >&2; exit 92; }
        psql -X -q -h /tmp -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE ROLE aluno LOGIN" -c "CREATE DATABASE exercicio OWNER aluno" >/dev/null
        if [ -n "${1:-}" ]; then
          printf '%s' "$1" | psql -X -q -h /tmp -U aluno -d exercicio -v ON_ERROR_STOP=1 -f - >/dev/null 2>/tmp/setup.err || { echo "[preparação do exercício falhou]" >&2; cat /tmp/setup.err >&2; exit 90; }
        fi
        exec psql -X -q -A -F '|' -P footer=off -h /tmp -U aluno -d exercicio -v ON_ERROR_STOP=1 -f -
        """;

    public string Language => "sql";

    public Task<SandboxResult> RunAsync(string code, string? setup = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var request = new SandboxRequest(
            Image,
            // sh -c SCRIPT NOME $1: "sh" vira o $0 do script e a preparação vira o $1.
            ["sh", "-c", Script, "sh", setup ?? string.Empty],
            Stdin: code);
        return sandbox.ExecuteAsync(request, cancellationToken);
    }
}
