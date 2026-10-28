using DevPath.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevPath.Web.Health;

/// <summary>
/// Verifica se a aplicação alcança o PostgreSQL. Usado pelo GET /health (healthcheck do Docker, issue #21).
/// </summary>
/// <remarks>
/// Não expõe detalhes do erro na resposta: o /health é público e devolve só Healthy/Unhealthy.
/// </remarks>
public sealed class DatabaseHealthCheck(DevPathDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Banco de dados indisponível.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Banco de dados indisponível.", ex);
        }
    }
}
