using System.Text.Json.Serialization;

namespace DevPath.Application.CodeExecution;

/// <summary>Como terminou a execução, do ponto de vista do aluno.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionStatus>))]
public enum ExecutionStatus
{
    /// <summary>Rodou e todos os casos de teste passaram.</summary>
    [JsonStringEnumMemberName("pass")] Pass,

    /// <summary>Rodou, mas o resultado não é o esperado em pelo menos um caso.</summary>
    [JsonStringEnumMemberName("fail")] Fail,

    /// <summary>Não rodou até o fim: erro de SQL, comando proibido, erro do exercício, executor com problema...</summary>
    [JsonStringEnumMemberName("error")] Error,

    /// <summary>Demorou demais e foi interrompida.</summary>
    [JsonStringEnumMemberName("timeout")] Timeout,

    /// <summary>
    /// O aluno já usou todas as tentativas (<c>Exercise.MaxAttempts</c>). Nada foi gravado e
    /// <c>remainingAttempts</c> vem 0. Não é 429: o 429 é só do rate limit.
    /// </summary>
    [JsonStringEnumMemberName("attempts_exhausted")] AttemptsExhausted
}

/// <summary>
/// Correção de UM caso de teste. Nunca traz as linhas esperadas nem a solução: só se passou e uma frase
/// sobre o que está diferente (ex.: "devolveu 3 linhas, mas o esperado é 2").
/// </summary>
/// <param name="Name">Nome do caso (vem do conteúdo, ex.: "lista título e preço").</param>
/// <param name="Passed">O caso passou.</param>
/// <param name="Message">Nulo quando passou; senão, o que está diferente ou por que não rodou.</param>
public sealed record TestCaseFeedback(string Name, bool Passed, string? Message);

/// <summary>
/// Resposta de <c>POST /api/exercicios/{exerciseId}/executar</c>: o que a tela do exercício mostra.
/// O mesmo objeto viaja do Worker para a Web pelo Redis (JSON camelCase).
/// </summary>
/// <param name="Columns">Nomes das colunas do resultado do ALUNO no primeiro caso (vazio se não houve tabela).</param>
/// <param name="Rows">
/// Linhas do resultado do ALUNO, no máximo <see cref="ExecutionLimits.MaxRowsShown"/>. Cada célula vem como texto
/// do jeito que o PostgreSQL devolveu ("29.90", "true", "Dom Casmurro"); NULL vem como <c>null</c>.
/// </param>
/// <param name="Truncated">Havia mais linhas do que as mostradas.</param>
/// <param name="FriendlyMessage">Frase curta para iniciante, sempre preenchida.</param>
/// <param name="TechnicalDetail">Mensagem original do PostgreSQL (a tela mostra recolhida em "Ver detalhe técnico").</param>
/// <param name="TestCases">Um item por caso de teste do exercício, na ordem do conteúdo.</param>
/// <param name="SubmissionId">Id da CodeSubmission gravada; nulo quando nada foi gravado (tentativas esgotadas, erro do exercício).</param>
/// <param name="RemainingAttempts">Tentativas que ainda restam; nulo quando o exercício não tem limite.</param>
public sealed record ExecutionOutcome(
    ExecutionStatus Status,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated,
    string FriendlyMessage,
    string? TechnicalDetail,
    int ExecutionTimeMs,
    int PassedCases,
    int TotalCases,
    IReadOnlyList<TestCaseFeedback> TestCases,
    Guid? SubmissionId,
    int? RemainingAttempts)
{
    /// <summary>Resultado sem tabela (recusado antes de executar, erro do exercício, executor fora do ar...).</summary>
    public static ExecutionOutcome Failure(ExecutionStatus status, string friendlyMessage, string? technicalDetail = null,
        int? remainingAttempts = null) =>
        new(status, [], [], false, friendlyMessage, technicalDetail, 0, 0, 0, [], null, remainingAttempts);

    /// <summary>Tentativas esgotadas: status próprio, mensagem amigável e <c>remainingAttempts</c> 0.</summary>
    public static ExecutionOutcome AttemptsExhausted(int maxAttempts) =>
        Failure(ExecutionStatus.AttemptsExhausted, AttemptsExhaustedMessage(maxAttempts), remainingAttempts: 0);

    public static string AttemptsExhaustedMessage(int maxAttempts) => maxAttempts == 1
        ? "Você já usou a única tentativa deste exercício. Revise a aula para fixar o conteúdo e siga para o próximo."
        : $"Você já usou as {maxAttempts} tentativas deste exercício. Revise a aula para fixar o conteúdo e siga para o próximo.";
}

/// <summary>Um pedido de execução que a Web coloca na fila e o Worker consome.</summary>
/// <param name="DeadlineUtc">Até quando a Web espera. O Worker descarta jobs vencidos e responde antes disso.</param>
public sealed record ExecutionJob(Guid JobId, Guid UserId, Guid ExerciseId, string Code, DateTime DeadlineUtc);

/// <summary>
/// Fila de execução (Web → Worker). A Web nunca executa código: só enfileira e espera a resposta (ADR-006).
/// </summary>
public interface IExecutionQueue
{
    /// <summary>Enfileira e espera o Worker responder.</summary>
    /// <exception cref="ExecutionUnavailableException">Ninguém respondeu a tempo (Worker parado ou sobrecarregado) ou Redis fora do ar.</exception>
    Task<ExecutionOutcome> ExecuteAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default);
}

/// <summary>O executor (Worker/Redis) não respondeu. Falha da plataforma, não do aluno.</summary>
public sealed class ExecutionUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Limites que a Web e o Worker conferem igual.</summary>
public static class ExecutionLimits
{
    /// <summary>Tamanho máximo do código enviado. Barra envios gigantes antes de gastar um container.</summary>
    public const int MaxCodeLength = 20_000;

    /// <summary>Máximo de linhas do resultado que vão para a tela (o resto marca <c>truncated</c>).</summary>
    public const int MaxRowsShown = 200;

    /// <summary>
    /// Corta a tabela para a tela. Só mexe no que o ALUNO produziu: o resultado esperado nunca passa por aqui.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<string?>> Rows, bool Truncated) CapRows(QueryResult result, int maxRows = MaxRowsShown)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
        var rows = result.Rows
            .Take(maxRows)
            .Select(r => (IReadOnlyList<string?>)r.Select(v => v.Kind == SqlValueKind.Null ? null : v.Raw).ToList())
            .ToList();
        return (rows, result.Truncated || result.Rows.Count > maxRows);
    }
}
