using System.Text.Json;
using DevPath.Application.CodeExecution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// Lado da Web da fila de execução: coloca o job no Redis e espera o Worker responder.
/// </summary>
/// <remarks>
/// Passo a passo (ADR-006):
/// 1) assina o canal "pronto" DESTE job (antes de enfileirar, para não perder o aviso);
/// 2) LPUSH do job na lista e PUBLISH no canal "novo" (acorda o Worker na hora);
/// 3) espera o aviso ou o tempo-limite;
/// 4) lê o resultado na chave do job. Se ela não existe, o Worker não respondeu a tempo.
/// Por que não BLPOP? A biblioteca StackExchange.Redis compartilha UMA conexão entre todas as requisições
/// e não aceita comandos bloqueantes (eles travariam todo mundo). O Pub/Sub dá o mesmo efeito sem bloquear.
/// </remarks>
public sealed class RedisExecutionQueue(
    IConnectionMultiplexer redis,
    IOptions<ExecutionQueueOptions> options,
    ILogger<RedisExecutionQueue> logger) : IExecutionQueue
{
    private readonly ExecutionQueueOptions _options = options.Value;

    public async Task<ExecutionOutcome> ExecuteAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default)
    {
        var job = new ExecutionJob(Guid.NewGuid(), userId, exerciseId, code, DateTime.UtcNow + _options.ResultTimeout);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = RedisChannel.Literal(_options.ResultChannel(job.JobId));

        try
        {
            var subscriber = redis.GetSubscriber();
            await subscriber.SubscribeAsync(channel, (_, _) => done.TrySetResult());
            try
            {
                var db = redis.GetDatabase();
                await db.ListLeftPushAsync(_options.QueueKey, JsonSerializer.Serialize(job, ExecutionJson.Options));
                await subscriber.PublishAsync(RedisChannel.Literal(_options.NewJobChannel), job.JobId.ToString("N"));

                try
                {
                    await done.Task.WaitAsync(_options.ResultTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    // Sem aviso a tempo. Ainda olhamos a chave: o aviso pode ter se perdido.
                }

                var value = await db.StringGetAsync(_options.ResultKey(job.JobId));
                if (value.IsNullOrEmpty)
                {
                    logger.LogWarning("Job {JobId} sem resposta do Worker em {Timeout}", job.JobId, _options.ResultTimeout);
                    throw new ExecutionUnavailableException("O Worker de execução não respondeu a tempo.");
                }

                return JsonSerializer.Deserialize<ExecutionOutcome>(value.ToString(), ExecutionJson.Options)
                    ?? throw new ExecutionUnavailableException("Resposta do Worker vazia.");
            }
            finally
            {
                await subscriber.UnsubscribeAsync(channel);
            }
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Redis indisponível ao enfileirar o job {JobId}", job.JobId);
            throw new ExecutionUnavailableException("Redis indisponível.", ex);
        }
    }
}
