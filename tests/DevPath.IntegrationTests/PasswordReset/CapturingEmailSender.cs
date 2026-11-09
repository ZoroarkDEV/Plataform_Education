using System.Collections.Concurrent;
using DevPath.Application.Email;

namespace DevPath.IntegrationTests.PasswordReset;

/// <summary>Dublê do <see cref="IEmailSender"/>: guarda os e-mails em vez de enviar.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Atraso simulado do SMTP (para provar que o POST não espera o envio).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        _sent.Enqueue(message);
    }

    public IReadOnlyList<EmailMessage> SentTo(string to) =>
        _sent.Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Espera o n-ésimo e-mail para <paramref name="to"/> (o envio é em segundo plano).</summary>
    public async Task<EmailMessage> WaitForAsync(string to, int count = 1, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var sent = SentTo(to);
            if (sent.Count >= count)
            {
                return sent[count - 1];
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Nenhum e-mail nº {count} para {to} em {timeout ?? TimeSpan.FromSeconds(10)}.");
    }
}
