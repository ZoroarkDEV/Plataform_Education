namespace DevPath.Infrastructure.Email;

/// <summary>
/// Linha única da tabela <c>EmailSettings</c> (#47): o que o admin salvou na tela.
/// A chave do Resend fica SÓ protegida (<see cref="ResendApiKeyProtected"/>, ASP.NET Data Protection);
/// as chaves do Data Protection ficam fora do banco (volume <c>web_keys</c>), então um dump do banco não a revela.
/// </summary>
public sealed class EmailSettingsRecord
{
    /// <summary>Sempre 1 (uma linha só; o banco garante com CHECK).</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary><c>None</c>, <c>Smtp</c> ou <c>Resend</c>.</summary>
    public string Provider { get; set; } = EmailProviders.None;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    /// <summary>Chave do Resend protegida (texto opaco do Data Protection). <c>null</c> = sem chave salva.</summary>
    public string? ResendApiKeyProtected { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Quem salvou por último (vira <c>null</c> se a conta for apagada).</summary>
    public Guid? UpdatedByUserId { get; set; }
}
