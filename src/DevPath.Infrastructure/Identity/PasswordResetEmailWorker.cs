using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Identity;

/// <summary>Processa a <see cref="PasswordResetQueue"/>: um escopo (DbContext/UserManager) por pedido.</summary>
internal sealed class PasswordResetEmailWorker(
    PasswordResetQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<PasswordResetService>()
                        .SendResetEmailAsync(email, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // O e-mail do aluno não vai para o log (dado pessoal); o erro sim.
                    logger.LogError(ex, "Falha ao enviar o e-mail de recuperação de senha.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }
}
