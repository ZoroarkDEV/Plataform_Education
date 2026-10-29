using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using FluentAssertions;

namespace DevPath.IntegrationTests.CodeExecution;

/// <summary>
/// O runner de SQL com o PostgreSQL de verdade dentro do sandbox. A segunda metade prova o isolamento
/// pedido pelo QA na issue #13: cada tentativa de virar superusuário, rodar programa ou ler arquivo falha,
/// e o aluno recebe uma mensagem amigável.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class PostgresSqlCodeRunnerTests
{
    // Folga para máquina carregada: o container tem 30 s; cada instrução do aluno, 3 s (o padrão).
    private static readonly SandboxLimits Relaxed = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly PostgresSqlCodeRunner _runner = new(DockerCliSandboxTests.CreateSandbox(), limits: Relaxed);

    private const string Setup = """
        CREATE TABLE alunos (nome text, nota numeric(4,2), ativo boolean);
        INSERT INTO alunos VALUES ('Bia', 8, true), ('Ana', 9.5, false), ('Caio', NULL, true);
        """;

    private async Task<SandboxResult> RunAsync(string code, string? setup = Setup) => await _runner.RunAsync(code, setup);

    [DockerFact]
    public async Task ConsultaDoAluno_DevolveColunasEValoresComTipo()
    {
        var result = await RunAsync("SELECT nome, nota, ativo FROM alunos ORDER BY nome;");

        result.Succeeded.Should().BeTrue(result.Stderr);
        var parsed = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows);
        parsed.Columns.Should().Equal("nome", "nota", "ativo");
        parsed.Rows.Should().HaveCount(3);
        parsed.Rows[0].Should().Equal(SqlValue.Text("Ana"), new SqlValue(SqlValueKind.Number, "9.50"), SqlValue.Bool(false));
        parsed.Rows[2][1].Should().Be(SqlValue.Null);
        parsed.Truncated.Should().BeFalse();
    }

    [DockerFact]
    public async Task ResultadoVazio_AindaTrazOsNomesDasColunas()
    {
        var result = await RunAsync("SELECT nome AS quem FROM alunos WHERE false");

        result.Succeeded.Should().BeTrue(result.Stderr);
        var parsed = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows);
        parsed.Columns.Should().Equal("quem");
        parsed.Rows.Should().BeEmpty();
    }

    [DockerFact]
    public async Task MuitasLinhas_CortaNoLimiteEMarcaTruncated()
    {
        var result = await RunAsync("SELECT n FROM generate_series(1, 5000) AS n", setup: null);

        result.Succeeded.Should().BeTrue(result.Stderr);
        var parsed = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows);
        parsed.Rows.Should().HaveCount(PostgresSqlCodeRunner.MaxRows);
        parsed.Truncated.Should().BeTrue();
    }

    [DockerFact]
    public async Task ErroDeSintaxe_DevolveCodigo3EMensagemAmigavel()
    {
        var result = await RunAsync("SELEC * FROM alunos;");

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SqlErrorExitCode);
        var error = SqlErrorTranslator.Translate(result.Stderr);
        error.SqlState.Should().Be("42601");
        error.FriendlyMessage.Should().Contain("erro de sintaxe");
    }

    [DockerFact]
    public async Task PreparacaoQuebrada_DevolveCodigoProprio()
    {
        var result = await RunAsync("SELECT 1;", "CREATE TABL x;");

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SetupFailedExitCode);
        result.Stderr.Should().Contain("preparação do exercício falhou");
    }

    [DockerFact]
    public async Task ConsultaLenta_EhCortadaPeloStatementTimeout()
    {
        var runner = new PostgresSqlCodeRunner(DockerCliSandboxTests.CreateSandbox(), TimeSpan.FromSeconds(1), Relaxed);

        var result = await runner.RunAsync("SELECT pg_sleep(20)");

        result.TimedOut.Should().BeFalse("quem corta é o banco, bem antes do timeout do container");
        SqlErrorTranslator.Translate(result.Stderr).IsTimeout.Should().BeTrue(result.Stderr);
    }

    /// <summary>
    /// Cada tentativa de escapar falha e vira uma mensagem amigável. Os meta-comandos do psql nem existem para
    /// o aluno: o código vai ao servidor como texto (\gexec), e lá "\" é só um erro de sintaxe.
    /// </summary>
    [DockerTheory]
    [InlineData(@"\c - postgres", SqlErrorTranslator.MetaCommandMessage)]
    [InlineData(@"\! id", SqlErrorTranslator.MetaCommandMessage)]
    [InlineData("SELECT 1; \\! id", SqlErrorTranslator.MetaCommandMessage)]
    [InlineData("SELECT 1;\n\\! id\nSELECT 2", SqlErrorTranslator.MetaCommandMessage)]
    [InlineData("SELECT 1; SELECT 2", SqlErrorTranslator.SingleStatementMessage)]
    [InlineData("COPY (SELECT 1) TO PROGRAM 'id'", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("SET ROLE postgres", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("ALTER ROLE aluno SUPERUSER", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("DELETE FROM alunos", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("SELECT pg_read_file('/etc/passwd')", SqlErrorTranslator.PermissionMessage)]
    // As mesmas ideias por caminhos que PASSAM pelo validador (um SELECT só): quem barra é a permissão do banco.
    [InlineData("SELECT set_config('role', 'postgres', false)", SqlErrorTranslator.PermissionMessage)]
    [InlineData("SELECT rolpassword FROM pg_authid", SqlErrorTranslator.PermissionMessage)]
    [InlineData("SELECT pg_read_file('/tmp/su.pw')", SqlErrorTranslator.PermissionMessage)]
    [InlineData("SELECT current_setting('data_directory')", SqlErrorTranslator.PermissionMessage)]
    // Tentativa de "fechar" o SELECT * FROM (...) e injetar instruções: o validador recusa antes.
    [InlineData("SELECT 1) x; COPY (SELECT 1) TO PROGRAM 'id'; SELECT * FROM (SELECT 1", "Há um erro de sintaxe")]
    public async Task TentativaDeEscapar_FalhaComMensagemAmigavel(string code, string friendly)
    {
        var result = await RunAsync(code);

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SqlErrorExitCode, result.Stderr);
        SqlErrorTranslator.Translate(result.Stderr).FriendlyMessage.Should().StartWith(friendly, result.Stderr);
        // Nada rodou no sistema: "id" imprimiria "uid=..." e /etc/passwd tem "root:".
        (result.Stdout + result.Stderr).Should().NotContain("uid=").And.NotContain("root:");
    }

    /// <summary>
    /// A garantia de verdade não é o validador nem o erro de sintaxe: é o PAPEL do aluno no banco. Mesmo que
    /// alguém achasse um jeito de mandar COPY ... TO PROGRAM direto, o PostgreSQL negaria, porque o papel não é
    /// superusuário nem membro de pg_execute_server_program. Este teste lê isso do próprio banco, como aluno.
    /// </summary>
    [DockerFact]
    public async Task AlunoNaoEhSuperusuario_CopyToProgramEhNegado()
    {
        var result = await RunAsync("""
            SELECT current_user,
                   r.rolsuper, r.rolcreaterole, r.rolcreatedb, r.rolreplication, r.rolbypassrls,
                   pg_has_role(current_user, 'pg_execute_server_program', 'USAGE') AS roda_programa,
                   pg_has_role(current_user, 'pg_read_server_files', 'USAGE') AS le_arquivo,
                   pg_has_role(current_user, 'pg_write_server_files', 'USAGE') AS grava_arquivo,
                   pg_has_role(current_user, 'postgres', 'USAGE') AS vira_postgres,
                   has_table_privilege('alunos', 'SELECT') AS pode_select,
                   has_table_privilege('alunos', 'INSERT') OR has_table_privilege('alunos', 'UPDATE')
                     OR has_table_privilege('alunos', 'DELETE') OR has_table_privilege('alunos', 'TRUNCATE') AS pode_alterar,
                   has_schema_privilege('public', 'CREATE') AS cria_tabela,
                   has_database_privilege(current_database(), 'CREATE') OR has_database_privilege(current_database(), 'TEMP') AS cria_schema,
                   current_setting('default_transaction_read_only') AS so_leitura
            FROM pg_roles r WHERE r.rolname = current_user
            """);

        result.Succeeded.Should().BeTrue(result.Stderr);
        var row = QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows).Rows.Single();
        row.Should().Equal(
            SqlValue.Text("aluno"),
            SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false),
            SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false),
            SqlValue.Bool(true), SqlValue.Bool(false), SqlValue.Bool(false), SqlValue.Bool(false),
            SqlValue.Text("on"));

        // E o COPY de fato não roda.
        var copy = await RunAsync("COPY (SELECT 1) TO PROGRAM 'id'");
        copy.ExitCode.Should().Be(PostgresSqlCodeRunner.SqlErrorExitCode);
        (copy.Stdout + copy.Stderr).Should().NotContain("uid=");
    }

    [DockerFact]
    public async Task DatasetReal_Livraria_FuncionaComAsPermissoesDoAluno()
    {
        // O arquivo de verdade de content/datasets (#22): roda como superusuário e o aluno consegue só ler.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "datasets", "livraria.sql")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("o teste roda de dentro do repositório");
        var sql = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "content", "datasets", "livraria.sql"));

        var result = await RunAsync("SELECT count(*) > 0 AS tem_livros, bool_and(preco > 0) AS precos FROM livros", sql);

        result.Succeeded.Should().BeTrue(result.Stderr);
        QueryResultParser.Parse(result.Stdout, PostgresSqlCodeRunner.MaxRows).Rows.Single()
            .Should().Equal(SqlValue.Bool(true), SqlValue.Bool(true));
    }

    [DockerFact]
    public async Task CodigoComAspasECifrao_NaoQuebraNada()
    {
        var result = await RunAsync("SELECT 'it''s $(id) `id` %s :codigo' AS t, $$\\! id$$ AS u");

        result.Succeeded.Should().BeTrue(result.Stderr);
        QueryResultParser.Parse(result.Stdout, 200).Rows.Single()
            .Should().Equal(SqlValue.Text("it's $(id) `id` %s :codigo"), SqlValue.Text("\\! id"));
    }
}
