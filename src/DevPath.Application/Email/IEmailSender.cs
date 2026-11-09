namespace DevPath.Application.Email;

/// <summary>Um e-mail pronto para enviar: sempre com versão HTML e versão texto (clientes sem HTML).</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Envio de e-mail. A implementação vem da Infrastructure (SMTP, configurado em <c>Email:Smtp:*</c>).
/// Em dev e staging o SMTP é o Mailpit do docker-compose; o provedor de produção é decisão do dono
/// (veja docs/email-e-recuperacao-de-senha.md).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
