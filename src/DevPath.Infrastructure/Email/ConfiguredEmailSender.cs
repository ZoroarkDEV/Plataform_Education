using DevPath.Application.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// O <see cref="IEmailSender"/> da aplicação (#47): a cada envio pergunta ao <see cref="EmailSettingsResolver"/>
/// qual provedor, remetente e chave valem e repassa para o SMTP ou o Resend. Assim o que o admin salva na tela
/// vale no próximo e-mail, sem rebuild nem reinício.
/// </summary>
/// <remarks>
/// Configuração incompleta (ex.: Resend sem chave) NÃO derruba o startup: o envio lança
/// <see cref="EmailDeliveryException"/> dizendo o que falta (o worker do #44 registra e segue).
/// Provedor <c>None</c>: só avisa no log, como no #44.
/// </remarks>
public sealed class ConfiguredEmailSender(
    EmailSettingsResolver resolver,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory) : IEmailSender
{
    private readonly UnconfiguredEmailSender _none = new(loggerFactory.CreateLogger<UnconfiguredEmailSender>());

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = await resolver.GetAsync(cancellationToken);
        if (settings.Provider == EmailProviders.None)
        {
            await _none.SendAsync(message, cancellationToken);
            return;
        }

        if (!settings.CanSend)
        {
            throw new EmailDeliveryException(
                $"E-mail não enviado: configuração de e-mail incompleta ({settings.Provider}). " +
                string.Join(" ", settings.MissingRequirements));
        }

        // Um escopo por envio: o HttpClient tipado do Resend é transiente (o IHttpClientFactory recicla os handlers).
        await using var scope = scopeFactory.CreateAsyncScope();
        if (settings.Provider == EmailProviders.Resend)
        {
            await scope.ServiceProvider.GetRequiredService<ResendEmailSender>()
                .SendAsync(message, settings.ToTransport(), cancellationToken);
        }
        else
        {
            await scope.ServiceProvider.GetRequiredService<SmtpEmailSender>()
                .SendAsync(message, settings.ToTransport(), cancellationToken);
        }
    }
}
