using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace DevPath.Web.Api;

/// <summary>Limites de execução por aluno (seção <c>RateLimiting:Execution</c> do appsettings).</summary>
public sealed class ExecutionRateLimitOptions
{
    public const string SectionName = "RateLimiting:Execution";

    /// <summary>Quantas execuções cada aluno pode fazer por janela.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Tamanho da janela, em segundos.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Em quantos pedaços a janela "desliza" (6 = a cada 10 s libera o que foi usado há 1 min).</summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>Execuções ao mesmo tempo por aluno (1 = precisa esperar a anterior terminar).</summary>
    public int ConcurrentPerUser { get; set; } = 1;

    /// <summary>Retry-After quando o bloqueio é por concorrência (não há como saber quando a outra termina).</summary>
    public int ConcurrencyRetryAfterSeconds { get; set; } = 2;
}

/// <summary>Corpo do 429: <c>{ "message": "...", "retryAfterSeconds": 6 }</c>.</summary>
public sealed record RateLimitedResponse(string Message, int RetryAfterSeconds);

/// <summary>Marca, nos metadados do endpoint, quem usa o limite de execução.</summary>
public sealed class ExecutionRateLimitedAttribute : Attribute;

/// <summary>
/// Rate limiting das execuções de código: cada execução gasta um container, então um aluno (ou script)
/// não pode monopolizar o Worker. Dois limites encadeados, os dois POR USUÁRIO:
/// 1) janela deslizante: no máximo N execuções por minuto;
/// 2) concorrência: no máximo 1 execução em andamento (sem fila: a segunda é recusada na hora).
/// </summary>
public static class ExecutionRateLimiting
{
    public const string RejectedMessage =
        "Calma! Você enviou muitas execuções seguidas. Espere alguns segundos e tente de novo.";

    public const string ConcurrentMessage =
        "Sua execução anterior ainda está rodando. Espere ela terminar e tente de novo.";

    public static IServiceCollection AddExecutionRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExecutionRateLimitOptions>(configuration.GetSection(ExecutionRateLimitOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // GlobalLimiter roda em toda requisição, mas só limita os endpoints marcados (o resto passa direto).
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (!IsLimited(context))
                    {
                        return RateLimitPartition.GetNoLimiter(string.Empty);
                    }
                    var limits = Limits(context);
                    return RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        SegmentsPerWindow = limits.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (!IsLimited(context))
                    {
                        return RateLimitPartition.GetNoLimiter(string.Empty);
                    }
                    var limits = Limits(context);
                    return RateLimitPartition.GetConcurrencyLimiter(PartitionKey(context), _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = limits.ConcurrentPerUser,
                        QueueLimit = 0
                    });
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Quem recusou? A janela deslizante marca RETRY_AFTER; a concorrência, REASON_PHRASE. O lease
                // encadeado (CreateChained) expõe só os NOMES dos metadados, não os valores, então o tempo vem
                // da configuração: na janela, um segmento (é quando a permissão mais antiga começa a voltar).
                var limits = Limits(context.HttpContext);
                var byWindow = context.Lease.MetadataNames.Contains(MetadataName.RetryAfter.Name);
                var retryAfter = byWindow
                    ? (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var exact) && exact > TimeSpan.Zero
                        ? exact
                        : TimeSpan.FromSeconds((double)limits.WindowSeconds / Math.Max(1, limits.SegmentsPerWindow)))
                    : TimeSpan.FromSeconds(limits.ConcurrencyRetryAfterSeconds);
                var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new RateLimitedResponse(byWindow ? RejectedMessage : ConcurrentMessage, seconds), cancellationToken);
            };
        });

        return services;
    }

    private static bool IsLimited(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ExecutionRateLimitedAttribute>() is not null;

    private static ExecutionRateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<ExecutionRateLimitOptions>>().Value;

    // Por usuário logado. O endpoint exige login, então o IP é só uma rede de segurança.
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress}";
}
