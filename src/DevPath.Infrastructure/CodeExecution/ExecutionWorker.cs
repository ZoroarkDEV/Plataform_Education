using System.Text.Json;
using DevPath.Application.CodeExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>
/// O laço do Worker: tira jobs da fila do Redis, executa no sandbox e devolve o resultado.
/// Roda só no processo DevPath.Worker (registrado por <c>AddSandboxExecution</c>).
/// </summary>
/// <remarks>
/// Roda <c>Sandbox:MaxConcurrency</c> laços em paralelo (um job por laço). Cada laço faz RPOP; se a fila
/// está vazia, dorme até um aviso no canal "novo" ou até <c>PollIntervalMilliseconds</c>.
/// Entrega "no máximo uma vez": se o Worker cair no meio de um job, o job se perde, e tudo bem, porque
/// a Web já devolveu 503 ao aluno, que pode clicar de novo (ver ADR-006).
/// </remarks>
public sealed class ExecutionWorker(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopes,
    IOptions<ExecutionQueueOptions> queueOptions,
    IOptions<SandboxOptions> sandboxOptions,
    ILogger<ExecutionWorker> logger) : BackgroundService
{
    private readonly ExecutionQueueOptions _options = queueOptions.Value;
    private readonly SemaphoreSlim _wakeUp = new(0);

    /// <summary>Folga entre a resposta do Worker e o prazo da Web.</summary>
    public static readonly TimeSpan ReplyMargin = TimeSpan.FromSeconds(1);

    public const string ExerciseBrokenMessage =
        "Este exercício está com um problema de configuração (não é culpa sua). A tentativa não foi contada. Avise a equipe.";

    public const string SandboxDownMessage =
        "O executor de código está com problemas agora. Tente de novo em alguns minutos.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(_options.NewJobChannel), (_, _) =>
        {
            if (_wakeUp.CurrentCount == 0)
            {
                _wakeUp.Release();
            }
        });

        var loops = Math.Max(1, sandboxOptions.Value.MaxConcurrency);
        logger.LogInformation("Worker de execução ouvindo {Queue} com {Loops} laços", _options.QueueKey, loops);
        await Task.WhenAll(Enumerable.Range(0, loops).Select(_ => LoopAsync(stoppingToken)));
    }

    private async Task LoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await redis.GetDatabase().ListRightPopAsync(_options.QueueKey);
                if (item.IsNull)
                {
                    await _wakeUp.WaitAsync(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), stoppingToken);
                    continue;
                }

                await ProcessAsync(item.ToString(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Redis fora do ar ou erro inesperado: registra, espera um pouco e continua. O Worker não cai.
                logger.LogError(ex, "Erro no laço do Worker de execução");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    /// <summary>Processa um job. Público para facilitar testes.</summary>
    public async Task ProcessAsync(string json, CancellationToken stoppingToken)
    {
        var job = JsonSerializer.Deserialize<ExecutionJob>(json, ExecutionJson.Options);
        if (job is null)
        {
            logger.LogWarning("Job inválido descartado");
            return;
        }

        // Responde um pouco ANTES do prazo da Web, para o "demorou demais" chegar ao aluno em vez de um 503.
        var remaining = job.DeadlineUtc - DateTime.UtcNow - ReplyMargin;
        if (remaining <= TimeSpan.Zero)
        {
            // A Web já desistiu (ou vai desistir) de esperar: executar agora só gastaria um container.
            logger.LogInformation("Job {JobId} expirado descartado", job.JobId);
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(remaining);

        var outcome = await RunJobAsync(job, deadline.Token, stoppingToken);

        var db = redis.GetDatabase();
        await db.StringSetAsync(_options.ResultKey(job.JobId), JsonSerializer.Serialize(outcome, ExecutionJson.Options),
            TimeSpan.FromSeconds(_options.ResultTtlSeconds));
        await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(_options.ResultChannel(job.JobId)), "1");
    }

    /// <summary>Executa o job e transforma toda falha conhecida numa resposta amigável (o Worker nunca cai por um job).</summary>
    private async Task<ExecutionOutcome> RunJobAsync(ExecutionJob job, CancellationToken deadline, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IExerciseSubmissionService>();
            return await service.SubmitAsync(job.UserId, job.ExerciseId, job.Code, deadline);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Estourou o prazo do job (fila cheia ou vários casos de teste lentos). Nada foi gravado.
            return ExecutionOutcome.Failure(ExecutionStatus.Timeout, SqlErrorTranslator.TimeoutMessage);
        }
        catch (ArgumentException ex)
        {
            return ExecutionOutcome.Failure(ExecutionStatus.Error, ex.Message);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or DatasetNotFoundException)
        {
            // Erro de conteúdo (casos de teste ilegíveis, dataset ausente, linguagem sem runner): o aluno vê uma
            // frase neutra; o motivo fica no log para a equipe corrigir o conteúdo.
            logger.LogError(ex, "Erro de conteúdo: exercício {ExerciseId} não pôde ser executado (job {JobId}). A tentativa não foi contada.",
                job.ExerciseId, job.JobId);
            return ExecutionOutcome.Failure(ExecutionStatus.Error, ExerciseBrokenMessage);
        }
        catch (KeyNotFoundException ex)
        {
            // O exercício sumiu entre a checagem da Web e a execução (ex.: conteúdo recarregado).
            logger.LogWarning(ex, "Exercício {ExerciseId} não existe mais (job {JobId})", job.ExerciseId, job.JobId);
            return ExecutionOutcome.Failure(ExecutionStatus.Error, ExerciseBrokenMessage);
        }
        catch (SandboxUnavailableException ex)
        {
            logger.LogError(ex, "Sandbox indisponível no job {JobId}", job.JobId);
            return ExecutionOutcome.Failure(ExecutionStatus.Error, SandboxDownMessage);
        }
    }

    public override void Dispose()
    {
        _wakeUp.Dispose();
        base.Dispose();
    }
}
