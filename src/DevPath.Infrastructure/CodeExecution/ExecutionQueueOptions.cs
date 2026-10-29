namespace DevPath.Infrastructure.CodeExecution;

/// <summary>Configuração da fila Web → Worker (seção <c>Execution</c>).</summary>
public sealed class ExecutionQueueOptions
{
    public const string SectionName = "Execution";

    /// <summary>Lista do Redis usada como fila (a Web faz LPUSH, o Worker faz RPOP: o primeiro a entrar sai primeiro).</summary>
    public string QueueKey { get; set; } = "devpath:execucao:fila";

    /// <summary>Quanto a Web espera pelo resultado. Um pouco acima do timeout do sandbox (10 s).</summary>
    public int ResultTimeoutSeconds { get; set; } = 15;

    /// <summary>Por quanto tempo o resultado fica guardado no Redis (depois some sozinho).</summary>
    public int ResultTtlSeconds { get; set; } = 120;

    /// <summary>Se ninguém avisar que chegou job novo, o Worker olha a fila de novo depois deste intervalo.</summary>
    public int PollIntervalMilliseconds { get; set; } = 1000;

    public TimeSpan ResultTimeout => TimeSpan.FromSeconds(ResultTimeoutSeconds);

    // Nomes derivados da fila, para dois ambientes (ou testes) com filas diferentes não se misturarem.
    public string NewJobChannel => $"{QueueKey}:novo";
    public string ResultKey(Guid jobId) => $"{QueueKey}:resultado:{jobId:N}";
    public string ResultChannel(Guid jobId) => $"{QueueKey}:pronto:{jobId:N}";
}
