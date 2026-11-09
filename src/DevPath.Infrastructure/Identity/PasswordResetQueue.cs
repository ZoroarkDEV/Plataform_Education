using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Identity;

/// <summary>
/// Fila em memória dos pedidos de "Esqueci a senha". O POST só põe o e-mail aqui e responde: assim a
/// resposta tem o mesmo tempo exista a conta ou não (o envio por SMTP levaria centenas de ms só para
/// quem tem conta). Um pedido na fila se perde se a Web reiniciar; o aluno pede de novo.
/// </summary>
public sealed class PasswordResetQueue(ILogger<PasswordResetQueue> logger)
{
    /// <summary>Limite da fila; acima disso o pedido é descartado (o rate limit já segura antes).</summary>
    public const int Capacity = 1000;

    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    public void Enqueue(string email)
    {
        if (!_channel.Writer.TryWrite(email))
        {
            logger.LogWarning("Fila de recuperação de senha cheia: pedido descartado.");
        }
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
