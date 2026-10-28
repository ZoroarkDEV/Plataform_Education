namespace DevPath.Application.CodeExecution;

/// <summary>
/// Executa um comando isolado do servidor (o "sandbox"). É a fronteira de segurança do DevPath:
/// código de aluno NUNCA roda no processo da aplicação, só através desta interface.
/// </summary>
/// <remarks>
/// A implementação da PoC (<c>DockerCliSandbox</c>, na Infrastructure) cria um container efêmero
/// por execução. Veja o ADR-004 (docs/adr/ADR-004-sandbox-execucao-codigo.md).
/// </remarks>
public interface ICodeSandbox
{
    /// <summary>Executa e espera terminar, ou mata a execução quando o tempo-limite estoura.</summary>
    /// <exception cref="SandboxUnavailableException">Docker indisponível, imagem não permitida ou ausente.</exception>
    Task<SandboxResult> ExecuteAsync(SandboxRequest request, CancellationToken cancellationToken = default);
}

/// <summary>O que executar.</summary>
/// <param name="Image">Imagem do container. Só imagens da lista de permitidas (<c>Sandbox:AllowedImages</c>).</param>
/// <param name="Command">Comando e argumentos. Não passa por shell no host: cada item é um argumento.</param>
/// <param name="Stdin">Entrada padrão. O código do aluno vai por aqui, nunca nos argumentos.</param>
/// <param name="Limits">Limites desta execução; nulo usa os padrões configurados.</param>
public sealed record SandboxRequest(
    string Image,
    IReadOnlyList<string> Command,
    string? Stdin = null,
    SandboxLimits? Limits = null);

/// <summary>
/// Limites de recursos de uma execução. Os padrões seguem a SPEC §8 (timeout de 5 a 10s).
/// </summary>
public sealed record SandboxLimits
{
    /// <summary>Tempo máximo de parede, incluindo subir o container. Estourou: <c>docker kill</c>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Memória máxima (RAM). A swap é igual, então não há swap extra.</summary>
    public int MemoryMb { get; init; } = 256;

    /// <summary>Fração de CPU (1.0 = um núcleo).</summary>
    public double Cpus { get; init; } = 1.0;

    /// <summary>Número máximo de processos/threads dentro do container (barra fork bomb).</summary>
    public int PidsLimit { get; init; } = 64;

    /// <summary>Tamanho do /tmp em memória, o único lugar gravável.</summary>
    public int TmpfsSizeMb { get; init; } = 64;

    /// <summary>Máximo de caracteres lidos de stdout e de stderr (cada um). Passou: corta e mata.</summary>
    public int MaxOutputChars { get; init; } = 64 * 1024;
}

/// <summary>Resultado de uma execução.</summary>
/// <param name="ExitCode">Código de saída do processo (137 = morto por SIGKILL).</param>
/// <param name="TimedOut">O tempo-limite estourou e o container foi morto.</param>
/// <param name="OomKilled">Provável morte por falta de memória (exit 137 sem timeout; ver ADR-004).</param>
/// <param name="OutputTruncated">A saída passou do limite e foi cortada.</param>
/// <param name="ContainerName">Nome único do container (<c>devpath-sandbox-&lt;guid&gt;</c>), útil em logs.</param>
public sealed record SandboxResult(
    string Stdout,
    string Stderr,
    int ExitCode,
    TimeSpan Duration,
    bool TimedOut,
    bool OomKilled,
    bool OutputTruncated,
    string ContainerName)
{
    /// <summary>Terminou sozinho, com código 0 e sem nenhum limite estourado.</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut && !OomKilled && !OutputTruncated;
}

/// <summary>Falha da plataforma (não do aluno): Docker fora do ar, imagem ausente ou proibida.</summary>
public sealed class SandboxUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
