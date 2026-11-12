using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// Limite do "Enviar e-mail de teste" (#47): um por admin a cada <see cref="EmailOptions.TestEmailCooldownSeconds"/>
/// (em memória, por instância). Evita queimar a cota do provedor ou virar um disparador de e-mails.
/// </summary>
public sealed class TestEmailThrottle(IOptions<EmailOptions> options, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastByUser = new();

    /// <summary><c>null</c> = pode enviar (e já conta este envio); senão, quanto falta esperar.</summary>
    public TimeSpan? TryAcquire(Guid userId)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, options.Value.TestEmailCooldownSeconds));
        var now = time.GetUtcNow();
        while (true)
        {
            if (_lastByUser.TryGetValue(userId, out var last))
            {
                var wait = last + cooldown - now;
                if (wait > TimeSpan.Zero)
                {
                    return wait;
                }

                if (_lastByUser.TryUpdate(userId, now, last))
                {
                    return null;
                }
            }
            else if (_lastByUser.TryAdd(userId, now))
            {
                return null;
            }
        }
    }
}
