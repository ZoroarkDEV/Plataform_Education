using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using DevPath.Application.CodeExecution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Sandbox da PoC: um container Docker descartável por execução, chamado pelo Docker CLI.
/// </summary>
/// <remarks>
/// <para>
/// Por que o CLI e não a biblioteca Docker.DotNet? (detalhes no ADR-004)
/// 1) os parâmetros de isolamento ficam escritos exatamente como na documentação do Docker
///    (<c>--network none</c>, <c>--pids-limit</c>...), fáceis de revisar e de testar no terminal;
/// 2) mandar o código pelo stdin é trivial (<c>docker run -i</c>);
/// 3) nenhuma dependência nova. O custo é iniciar um processo a mais (alguns milissegundos).
/// </para>
/// <para>
/// Segurança do lado do host: os argumentos vão em <see cref="ProcessStartInfo.ArgumentList"/>
/// (sem shell, então não há como "injetar" comandos) e o código do aluno vai só pelo stdin.
/// </para>
/// </remarks>
public sealed class DockerCliSandbox : ICodeSandbox, IDisposable
{
    /// <summary>Código que o <c>docker run</c> devolve quando o problema é do próprio Docker.</summary>
    private const int DockerRunFailure = 125;

    /// <summary>128 + 9 (SIGKILL): o processo foi morto à força.</summary>
    private const int KilledBySigkill = 137;

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly SandboxOptions _options;
    private readonly ILogger<DockerCliSandbox> _logger;
    private readonly SemaphoreSlim _slots;

    public DockerCliSandbox(IOptions<SandboxOptions> options, ILogger<DockerCliSandbox> logger)
    {
        _options = options.Value;
        _logger = logger;
        _slots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
    }

    public async Task<SandboxResult> ExecuteAsync(SandboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.AllowedImages.Contains(request.Image, StringComparer.Ordinal))
        {
            throw new SandboxUnavailableException($"Imagem não permitida no sandbox: '{request.Image}'.");
        }

        var limits = request.Limits ?? _options.DefaultLimits;

        // Fila: no máximo MaxConcurrency containers ao mesmo tempo, para um pico de envios não derrubar o servidor.
        await _slots.WaitAsync(cancellationToken);
        try
        {
            return await RunContainerAsync(request, limits, cancellationToken);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Monta o <c>docker run</c>. Público para os testes conferirem cada flag de isolamento.</summary>
    public static IReadOnlyList<string> BuildRunArguments(string containerName, SandboxRequest request, SandboxLimits limits)
    {
        var memory = $"{limits.MemoryMb}m";
        var args = new List<string>
        {
            "run",
            "--rm",                                   // apaga o container quando termina
            "-i",                                     // mantém o stdin aberto: é por ele que o código entra
            "--name", containerName,                  // nome único: dá para matar exatamente este container
            "--label", "devpath.sandbox=true",        // facilita achar sobras: docker ps -a --filter label=devpath.sandbox
            "--network", "none",                      // sem rede: nada de baixar coisas ou atacar outros serviços
            "--memory", memory,
            "--memory-swap", memory,                  // igual à memória = zero swap extra
            "--cpus", limits.Cpus.ToString(CultureInfo.InvariantCulture),
            "--pids-limit", limits.PidsLimit.ToString(CultureInfo.InvariantCulture), // barra fork bomb
            "--read-only",                            // sistema de arquivos da imagem só leitura...
            "--tmpfs", $"/tmp:rw,noexec,nosuid,nodev,size={limits.TmpfsSizeMb}m", // ...exceto /tmp, em memória e sem executáveis
            "--user", "65534:65534",                  // usuário "nobody", nunca root
            "--cap-drop", "ALL",                      // nenhum poder especial do kernel
            "--security-opt", "no-new-privileges",    // nem via setuid dá para ganhar privilégio
            "--init",                                 // processo init pequeno: repassa sinais e recolhe zumbis
            "--pull", "never",                        // nunca baixa imagem durante uma execução
            "--log-driver", "none",                   // a saída vem pelo pipe; o Docker não guarda cópia
            request.Image
        };
        // Repare: nenhum "-p"/"--publish". O container não expõe porta nenhuma.
        args.AddRange(request.Command);
        return args;
    }

    private async Task<SandboxResult> RunContainerAsync(SandboxRequest request, SandboxLimits limits, CancellationToken cancellationToken)
    {
        var containerName = $"devpath-sandbox-{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo(_options.DockerPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // UTF-8 sem BOM nos dois sentidos: o runner de SQL manda o tamanho do código em BYTES UTF-8 na frente.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in BuildRunArguments(containerName, request, limits))
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new SandboxUnavailableException($"Docker CLI não encontrado em '{_options.DockerPath}'.", ex);
        }

        // "kill" dispara quando o tempo acaba OU a saída passa do limite. O cancelamento de quem chamou
        // (ex.: o aluno fechou a página) também mata, mas depois relançamos o OperationCanceledException.
        using var timeout = new CancellationTokenSource(limits.Timeout);
        using var outputLimit = new CancellationTokenSource();
        using var kill = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, outputLimit.Token, cancellationToken);

        var stdoutTask = ReadCappedAsync(process.StandardOutput, limits.MaxOutputChars, outputLimit);
        var stderrTask = ReadCappedAsync(process.StandardError, limits.MaxOutputChars, outputLimit);

        try
        {
            try
            {
                if (request.Stdin is not null)
                {
                    await process.StandardInput.WriteAsync(request.Stdin.AsMemory(), kill.Token);
                }
                process.StandardInput.Close(); // fim do stdin: o programa do container sabe que acabou a entrada
            }
            catch (IOException)
            {
                // O container já terminou (ex.: imagem não existe) antes de lermos tudo. O código de saída explica.
            }
            catch (OperationCanceledException)
            {
                // O tempo acabou no meio da escrita; o WaitForExitAsync abaixo cai direto no "docker kill".
            }

            var killed = false;
            try
            {
                await process.WaitForExitAsync(kill.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    killed = true;
                    // Mata o CONTAINER pelo nome (matar só o processo "docker" deixaria o container rodando).
                    await RunDockerQuietlyAsync("kill", containerName);
                }
                await WaitForExitOrKillClientAsync(process);
            }

            stopwatch.Stop();
            var (stdout, stdoutTruncated) = await stdoutTask;
            var (stderr, stderrTruncated) = await stderrTask;
            cancellationToken.ThrowIfCancellationRequested();

            // Só é timeout se o relógio estourou E fomos nós que matamos: um processo que terminou sozinho
            // no mesmo instante em que o tempo acabou não é "tempo esgotado" (falso positivo apontado pelo QA).
            var timedOut = killed && timeout.IsCancellationRequested;
            var truncated = stdoutTruncated || stderrTruncated;
            var exitCode = process.ExitCode;

            if (exitCode == DockerRunFailure && !timedOut && !truncated)
            {
                // 125 = o Docker não conseguiu nem criar o container (imagem ausente, daemon fora do ar...).
                _logger.LogError("Sandbox indisponível ({Container}): {Stderr}", containerName, stderr);
                throw new SandboxUnavailableException($"O Docker não conseguiu executar o container: {stderr.Trim()}");
            }

            // 137 sem timeout e sem corte de saída: quase sempre é o kernel matando por falta de memória.
            // Não dá para confirmar com "docker inspect" porque o --rm já apagou o container (ver ADR-004).
            var oomKilled = exitCode == KilledBySigkill && !timedOut && !truncated;

            if (timedOut || oomKilled || truncated)
            {
                _logger.LogInformation(
                    "Sandbox {Container} interrompido: timeout={TimedOut} oom={Oom} saidaCortada={Truncated}",
                    containerName, timedOut, oomKilled, truncated);
            }

            return new SandboxResult(stdout, stderr, exitCode, stopwatch.Elapsed, timedOut, oomKilled, truncated, containerName);
        }
        finally
        {
            // Rede de segurança: se algo deu errado no caminho, garante que o container não fica para trás.
            // Normalmente o --rm já apagou e este comando só responde "No such container".
            if (!process.HasExited)
            {
                TryKill(process);
            }
            await RunDockerQuietlyAsync("rm", "-f", containerName);
        }
    }

    /// <summary>
    /// Lê um stream guardando no máximo <paramref name="maxChars"/> caracteres. Passou disso, pede para matar
    /// o container e continua lendo (descartando), para o pipe não travar o processo do Docker.
    /// </summary>
    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(
        StreamReader reader, int maxChars, CancellationTokenSource onLimit)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var room = maxChars - builder.Length;
            if (read > room)
            {
                builder.Append(buffer, 0, Math.Max(0, room));
                if (!truncated)
                {
                    truncated = true;
                    await onLimit.CancelAsync();
                }
                continue;
            }
            builder.Append(buffer, 0, read);
        }
        return (builder.ToString(), truncated);
    }

    private static async Task WaitForExitOrKillClientAsync(Process process)
    {
        using var grace = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private async Task RunDockerQuietlyAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo(_options.DockerPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo)!;
            using var grace = new CancellationTokenSource(CleanupTimeout);
            var drainOut = process.StandardOutput.ReadToEndAsync(grace.Token);
            var drainErr = process.StandardError.ReadToEndAsync(grace.Token);
            await process.WaitForExitAsync(grace.Token);
            await Task.WhenAll(drainOut, drainErr);
        }
        catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Falha ao rodar 'docker {Args}'", string.Join(' ', args));
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Já terminou.
        }
    }

    public void Dispose() => _slots.Dispose();
}
