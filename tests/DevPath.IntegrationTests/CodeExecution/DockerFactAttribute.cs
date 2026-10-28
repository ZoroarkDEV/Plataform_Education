using System.Diagnostics;
using DevPath.Infrastructure.CodeExecution;

namespace DevPath.IntegrationTests.CodeExecution;

/// <summary>
/// Um [Fact] que vira "Skipped" (e não "Failed") quando não há Docker ou a imagem do sandbox não existe.
/// Assim a suíte roda em máquinas sem Docker e o motivo aparece no relatório do teste.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (DockerAvailability.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>O mesmo que <see cref="DockerFactAttribute"/>, para testes com [InlineData].</summary>
public sealed class DockerTheoryAttribute : TheoryAttribute
{
    public DockerTheoryAttribute()
    {
        if (DockerAvailability.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

internal static class DockerAvailability
{
    private static readonly Lazy<string?> Reason = new(Check);

    /// <summary>Nulo = pode rodar.</summary>
    public static string? SkipReason => Reason.Value;

    private static string? Check()
    {
        if (Run("version", "--format", "{{.Server.Version}}", TimeSpan.FromSeconds(20)) != 0)
        {
            return "Docker indisponível: testes do sandbox pulados.";
        }

        // O sandbox usa --pull never; nos testes, baixamos a imagem uma vez se ela não estiver no host.
        if (Run("image", "inspect", PostgresSqlCodeRunner.Image, TimeSpan.FromSeconds(20)) != 0
            && Run("pull", PostgresSqlCodeRunner.Image, TimeSpan.FromMinutes(3)) != 0)
        {
            return $"Imagem {PostgresSqlCodeRunner.Image} indisponível: testes do sandbox pulados.";
        }

        return null;
    }

    private static int Run(string a, string b, string? c, TimeSpan timeout) => RunArgs(timeout, c is null ? [a, b] : [a, b, c]);

    private static int Run(string a, string b, TimeSpan timeout) => RunArgs(timeout, [a, b]);

    private static int RunArgs(TimeSpan timeout, string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }
            using var process = Process.Start(info)!;
            process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return -1;
            }
            return process.ExitCode;
        }
        catch (Exception)
        {
            return -1; // docker nem instalado
        }
    }

    /// <summary>Quantos containers com o label do sandbox existem agora.</summary>
    public static int SandboxContainerCount()
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "ps", "-a", "-q", "--filter", "label=devpath.sandbox=true" })
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>O container ainda existe (rodando ou parado)? Usado para provar que ninguém ficou para trás.</summary>
    public static bool ContainerExists(string name)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "ps", "-a", "-q", "--filter", $"name=^/{name}$" })
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return !string.IsNullOrWhiteSpace(output);
    }
}

/// <summary>Testes que criam containers do sandbox rodam um de cada vez (mais estável e permite contar sobras).</summary>
[CollectionDefinition(Name)]
public sealed class SandboxCollection
{
    public const string Name = "Sandbox";
}
