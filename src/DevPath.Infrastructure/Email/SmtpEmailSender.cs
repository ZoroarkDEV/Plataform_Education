using DevPath.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DevPath.Infrastructure.Email;

/// <summary>Envio por SMTP com MailKit (corpo multipart: texto + HTML).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    /// <summary>Envia com o remetente de <see cref="EmailOptions"/> (uso direto/testes do #44).</summary>
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        return SendAsync(message, new EmailTransport(settings.FromAddress, settings.FromName, null), cancellationToken);
    }

    /// <summary>Envia com o remetente resolvido por envio (tela de admin, #47). Servidor SMTP: sempre <c>Email:Smtp:*</c>.</summary>
    internal async Task SendAsync(EmailMessage message, EmailTransport transport, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;

        var host = string.IsNullOrWhiteSpace(smtp.Host)
            ? throw new InvalidOperationException("Email:Smtp:Host não configurado.")
            : smtp.Host;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(transport.FromName, transport.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)TimeSpan.FromSeconds(smtp.TimeoutSeconds).TotalMilliseconds };
        await client.ConnectAsync(host, smtp.Port, ToMailKit(smtp.Security), cancellationToken);
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions ToMailKit(SmtpSecurity security) => security switch
    {
        SmtpSecurity.Auto => SecureSocketOptions.Auto,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None
    };
}
