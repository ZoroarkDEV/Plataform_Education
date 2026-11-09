using DevPath.Application.Email;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// Usado quando <c>Email:Smtp:Host</c> não está configurado (hoje: produção, até o dono escolher o
/// provedor). Não envia nada e avisa no log, SEM o conteúdo: o link de senha é um segredo.
/// </summary>
internal sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "E-mail \"{Subject}\" NÃO enviado: Email:Smtp:Host não está configurado (veja docs/email-e-recuperacao-de-senha.md).",
            message.Subject);
        return Task.CompletedTask;
    }
}
