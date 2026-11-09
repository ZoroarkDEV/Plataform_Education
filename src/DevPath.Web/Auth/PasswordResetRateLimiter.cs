using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace DevPath.Web.Auth;

/// <summary>Limites do POST "Esqueci a senha" (seção <c>RateLimiting:PasswordReset</c>).</summary>
public sealed class PasswordResetRateLimitOptions
{
    public const string SectionName = "RateLimiting:PasswordReset";

    /// <summary>Pedidos por e-mail por janela (US-AUTH.1: 3 por hora).</summary>
    public int PerEmailPermitLimit { get; set; } = 3;

    public int PerEmailWindowMinutes { get; set; } = 60;

    /// <summary>Pedidos por IP por janela (folgado: uma sala de aula pode sair pelo mesmo IP).</summary>
    public int PerIpPermitLimit { get; set; } = 20;

    public int PerIpWindowMinutes { get; set; } = 60;

    /// <summary>Em quantos pedaços a janela desliza.</summary>
    public int SegmentsPerWindow { get; set; } = 6;
}

/// <summary>
/// Rate limit do "Esqueci a senha", por IP e por e-mail, com os mesmos limitadores de janela deslizante
/// (System.Threading.RateLimiting) da execução de exercícios (<see cref="Api.ExecutionRateLimiting"/>).
/// </summary>
/// <remarks>
/// Não usa o middleware <c>UseRateLimiter</c> como a execução porque (1) a partição por e-mail precisa
/// do corpo do formulário, que o middleware não lê, e (2) a recusa aqui é uma página em PT-BR, não o
/// JSON da API. Os dois limites não revelam se a conta existe: contam igual para qualquer e-mail, e o
/// limite por e-mail devolve a MESMA resposta de sucesso (só não envia).
/// Em memória, por instância da Web (como o da execução; Redis fica para a fase 2, #28).
/// </remarks>
public sealed class PasswordResetRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _perIp;
    private readonly PartitionedRateLimiter<string> _perEmail;

    public PasswordResetRateLimiter(IOptions<PasswordResetRateLimitOptions> options)
    {
        var limits = options.Value;
        _perIp = Create(limits.PerIpPermitLimit, limits.PerIpWindowMinutes, limits.SegmentsPerWindow);
        _perEmail = Create(limits.PerEmailPermitLimit, limits.PerEmailWindowMinutes, limits.SegmentsPerWindow);
    }

    /// <summary>Consome uma permissão do IP. <c>false</c> = mostrar "muitos pedidos".</summary>
    public bool TryAcquireForIp(IPAddress? ip) => TryAcquire(_perIp, $"ip:{ip?.ToString() ?? "desconhecido"}");

    /// <summary>Consome uma permissão do e-mail (normalizado). <c>false</c> = não enviar, mesma resposta.</summary>
    public bool TryAcquireForEmail(string email) =>
        TryAcquire(_perEmail, $"email:{email.Trim().ToUpperInvariant()}");

    public void Dispose()
    {
        _perIp.Dispose();
        _perEmail.Dispose();
    }

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Create(int permitLimit, int windowMinutes, int segments) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                SegmentsPerWindow = Math.Max(1, segments),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
}

/// <summary>Registro do <see cref="PasswordResetRateLimiter"/> (singleton: os contadores são do processo).</summary>
public static class PasswordResetRateLimitingExtensions
{
    public static IServiceCollection AddPasswordResetRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PasswordResetRateLimitOptions>(configuration.GetSection(PasswordResetRateLimitOptions.SectionName));
        services.AddSingleton<PasswordResetRateLimiter>();
        return services;
    }
}
