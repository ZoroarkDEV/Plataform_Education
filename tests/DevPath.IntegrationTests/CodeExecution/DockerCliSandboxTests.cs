using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevPath.IntegrationTests.CodeExecution;

/// <summary>
/// Prova, com containers de verdade, que cada barreira do ADR-004 funciona.
/// Usa a própria imagem postgres:16-alpine (que tem sh e busybox) para não depender de outra imagem.
/// </summary>
[Collection(SandboxCollection.Name)]
public sealed class DockerCliSandboxTests
{
    private const string Image = PostgresSqlCodeRunner.Image;

    // Folga generosa: numa máquina ocupada só subir o container já leva alguns segundos.
    private static readonly SandboxLimits Relaxed = new() { Timeout = TimeSpan.FromSeconds(60) };

    internal static DockerCliSandbox CreateSandbox(params string[] extraImages) =>
        new(Options.Create(new SandboxOptions { AllowedImages = [Image, .. extraImages] }),
            NullLogger<DockerCliSandbox>.Instance);

    private static Task<SandboxResult> RunShAsync(string script, SandboxLimits? limits = null, string? stdin = null) =>
        CreateSandbox().ExecuteAsync(new SandboxRequest(Image, ["sh", "-c", script], stdin, limits ?? Relaxed));

    [DockerFact]
    public async Task Executa_DevolveStdoutStderrECodigoDeSaida()
    {
        var result = await RunShAsync("echo ola; echo problema >&2; exit 3");

        result.Stdout.Should().Be("ola\n");
        result.Stderr.Should().Be("problema\n");
        result.ExitCode.Should().Be(3);
        result.TimedOut.Should().BeFalse();
        result.OomKilled.Should().BeFalse();
        result.ContainerName.Should().MatchRegex("^devpath-sandbox-[0-9a-f]{32}$");
        result.Duration.Should().BePositive();
        DockerAvailability.ContainerExists(result.ContainerName).Should().BeFalse("o --rm apaga o container");
    }

    [DockerFact]
    public async Task CodigoEntraPeloStdin()
    {
        var result = await RunShAsync("cat", stdin: "SELECT 'aspas \"e\" $(cmd)';");

        result.Stdout.Should().Be("SELECT 'aspas \"e\" $(cmd)';", "o stdin não passa por shell nenhum");
        result.Succeeded.Should().BeTrue();
    }

    [DockerFact]
    public async Task Timeout_MataOContainer_ENaoDeixaSobra()
    {
        var result = await RunShAsync("sleep 120", new SandboxLimits { Timeout = TimeSpan.FromSeconds(3) });

        result.TimedOut.Should().BeTrue();
        result.OomKilled.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(60));
        DockerAvailability.ContainerExists(result.ContainerName).Should().BeFalse();
    }

    [DockerFact]
    public async Task CancelamentoDeQuemChamou_MataOContainer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sandbox = CreateSandbox();

        var act = () => sandbox.ExecuteAsync(new SandboxRequest(Image, ["sh", "-c", "sleep 120"], Limits: Relaxed), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Não sabemos o nome aqui, mas o label marca todo container do sandbox. Os testes de sandbox
        // rodam em sequência (mesma coleção), então não pode sobrar nenhum.
        DockerAvailability.SandboxContainerCount().Should().Be(0);
    }

    [DockerFact]
    public async Task SemRede_SoExisteLoopback_EConexaoFalha()
    {
        var result = await RunShAsync("ls /sys/class/net; wget -q -T 3 -O- http://1.1.1.1 >/dev/null 2>&1 && echo CONECTOU || echo SEM-REDE");

        result.Stdout.Should().Be("lo\nSEM-REDE\n");
    }

    [DockerFact]
    public async Task RaizSomenteLeitura_TmpGravavelMasSemExecucao()
    {
        var result = await RunShAsync("""
            touch /arquivo 2>/dev/null && echo raiz-gravavel || echo raiz-somente-leitura
            echo oi > /tmp/a && echo tmp-gravavel
            cp /bin/busybox /tmp/bb && (/tmp/bb true 2>/dev/null && echo tmp-executa || echo tmp-noexec)
            """);

        result.Stdout.Should().Be("raiz-somente-leitura\ntmp-gravavel\ntmp-noexec\n");
    }

    [DockerFact]
    public async Task UsuarioNaoRoot_SemCapabilities()
    {
        var result = await RunShAsync("id -u; id -g; grep CapEff /proc/self/status");

        result.Stdout.Should().Be("65534\n65534\nCapEff:\t0000000000000000\n");
    }

    [DockerFact]
    public async Task LimiteDeProcessos_BarraForkBomb()
    {
        // Tenta criar 100 processos com limite 32: o shell não consegue o fork e aborta antes do "fim".
        var result = await RunShAsync("""
            cat /sys/fs/cgroup/pids.max
            for i in $(seq 1 100); do sleep 30 & done
            echo fim
            """, Relaxed with { PidsLimit = 32 });

        result.Stdout.Should().Be("32\n", "o limite vale para o container inteiro e o \"fim\" nunca é alcançado");
        result.Stderr.Should().Contain("can't fork");
        result.ExitCode.Should().NotBe(0);
        result.TimedOut.Should().BeFalse();
    }

    [DockerFact]
    public async Task MemoriaDemais_EhMortaPorOom()
    {
        // Dobra uma string até estourar 64 MB.
        var result = await RunShAsync("awk 'BEGIN { s = \"x\"; while (1) s = s s }'", Relaxed with { MemoryMb = 64 });

        result.ExitCode.Should().Be(137);
        result.OomKilled.Should().BeTrue();
        result.TimedOut.Should().BeFalse();
    }

    [DockerFact]
    public async Task SaidaGigante_EhCortadaEOContainerMorto()
    {
        var result = await RunShAsync("yes devpath", Relaxed with { MaxOutputChars = 1_000 });

        result.OutputTruncated.Should().BeTrue();
        result.Stdout.Length.Should().Be(1_000);
        result.OomKilled.Should().BeFalse("morte por excesso de saída não é OOM");
        result.Succeeded.Should().BeFalse();
        DockerAvailability.ContainerExists(result.ContainerName).Should().BeFalse();
    }

    [DockerFact]
    public async Task ImagemPermitidaMasAusente_LancaSandboxUnavailable()
    {
        const string missing = "devpath-sandbox-imagem-inexistente:0";
        var sandbox = CreateSandbox(missing);

        var act = () => sandbox.ExecuteAsync(new SandboxRequest(missing, ["true"], Limits: Relaxed));

        await act.Should().ThrowAsync<SandboxUnavailableException>();
    }

    [Fact]
    public async Task ImagemForaDaLista_EhRecusadaSemChamarODocker()
    {
        var sandbox = new DockerCliSandbox(
            Options.Create(new SandboxOptions { DockerPath = "/caminho/que/nao/existe/docker" }),
            NullLogger<DockerCliSandbox>.Instance);

        var act = () => sandbox.ExecuteAsync(new SandboxRequest("ubuntu:latest", ["true"]));

        await act.Should().ThrowAsync<SandboxUnavailableException>().WithMessage("*não permitida*");
    }

    [Fact]
    public async Task DockerAusente_LancaSandboxUnavailable()
    {
        var sandbox = new DockerCliSandbox(
            Options.Create(new SandboxOptions { DockerPath = "/caminho/que/nao/existe/docker" }),
            NullLogger<DockerCliSandbox>.Instance);

        var act = () => sandbox.ExecuteAsync(new SandboxRequest(Image, ["true"]));

        await act.Should().ThrowAsync<SandboxUnavailableException>().WithMessage("*Docker CLI não encontrado*");
    }
}
