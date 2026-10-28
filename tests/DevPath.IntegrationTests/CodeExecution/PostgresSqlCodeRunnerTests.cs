using DevPath.Infrastructure.CodeExecution;
using FluentAssertions;

namespace DevPath.IntegrationTests.CodeExecution;

[Collection(SandboxCollection.Name)]
public sealed class PostgresSqlCodeRunnerTests
{
    private readonly PostgresSqlCodeRunner _runner = new(DockerCliSandboxTests.CreateSandbox());

    private const string Setup = "CREATE TABLE alunos (nome text, nota int); INSERT INTO alunos VALUES ('Bia', 8), ('Ana', 9);";

    [DockerFact]
    public async Task ConsultaDoAluno_RodaDepoisDaPreparacao()
    {
        var result = await _runner.RunAsync("SELECT nome, nota FROM alunos ORDER BY nome;", Setup);

        result.Succeeded.Should().BeTrue(result.Stderr);
        result.Stdout.Should().Be("nome|nota\nAna|9\nBia|8\n");
    }

    [DockerFact]
    public async Task ErroDeSintaxe_DevolveCodigo3EMensagemDoPostgres()
    {
        var result = await _runner.RunAsync("SELEC * FROM alunos;", Setup);

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SqlErrorExitCode);
        result.Stderr.Should().Contain("syntax error");
    }

    [DockerFact]
    public async Task PreparacaoQuebrada_DevolveCodigoProprio()
    {
        var result = await _runner.RunAsync("SELECT 1;", "CREATE TABL x;");

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SetupFailedExitCode);
        result.Stderr.Should().Contain("preparação do exercício falhou");
    }

    [DockerFact]
    public async Task AlunoNaoEhSuperusuario_CopyToProgramEhNegado()
    {
        var result = await _runner.RunAsync("COPY (SELECT 1) TO PROGRAM 'id';");

        result.ExitCode.Should().Be(PostgresSqlCodeRunner.SqlErrorExitCode);
        result.Stderr.Should().Contain("permission denied");
    }
}
