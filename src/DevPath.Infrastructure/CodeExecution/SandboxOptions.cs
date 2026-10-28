using DevPath.Application.CodeExecution;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>Configuração do sandbox (seção <c>Sandbox</c> do appsettings / variáveis <c>Sandbox__...</c>).</summary>
public sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    /// <summary>Caminho do executável do Docker CLI.</summary>
    public string DockerPath { get; set; } = "docker";

    /// <summary>
    /// Imagens que podem ser usadas. Qualquer outra é recusada antes de chamar o Docker.
    /// Como usamos <c>--pull never</c>, elas precisam estar baixadas no servidor (docker pull).
    /// </summary>
    public string[] AllowedImages { get; set; } = ["postgres:16-alpine"];

    /// <summary>Quantas execuções podem rodar ao mesmo tempo. As demais esperam na fila.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Limites usados quando a requisição não traz os seus.</summary>
    public SandboxLimits DefaultLimits { get; set; } = new();
}
