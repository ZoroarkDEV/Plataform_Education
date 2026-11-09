using DevPath.Application.Email;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// Usado com <c>Email:Provider=None</c> (ou Provider vazio sem <c>Email:Smtp:Host</c>): ex. testes, ou
/// produção antes de a chave do Resend chegar. Não envia nada e avisa no log, SEM o conteúdo: o link de senha é um segredo.
/// </summary>
internal sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "E-mail \"{Subject}\" NÃO enviado: Email:Provider=None ou Email:Smtp:Host vazio (veja docs/email-e-recuperacao-de-senha.md).",
            message.Subject);
        return Task.CompletedTask;
    }
}
