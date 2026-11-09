namespace DevPath.Infrastructure.Email;

/// <summary>Seção <c>Email</c> do appsettings / variáveis <c>Email__*</c>.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Remetente (ex.: nao-responda@devpath.app). Precisa ser aceito pelo provedor SMTP.</summary>
    public string FromAddress { get; set; } = "nao-responda@devpath.local";

    public string FromName { get; set; } = "DevPath";

    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>Seção <c>Email:Smtp</c>. Sem <see cref="Host"/>, nenhum e-mail é enviado (só um aviso no log).</summary>
public sealed class SmtpOptions
{
    /// <summary>Servidor SMTP. Em dev/staging: <c>mailpit</c> (compose) ou <c>localhost</c>.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 1025;

    /// <summary>
    /// <c>None</c> (Mailpit), <c>StartTls</c> (porta 587), <c>SslOnConnect</c> (porta 465) ou <c>Auto</c>.
    /// Fora do Mailpit use sempre TLS.
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.None;

    /// <summary>Usuário do SMTP. Vazio = sem autenticação (Mailpit).</summary>
    public string? Username { get; set; }

    /// <summary>Senha do SMTP: só por variável de ambiente/segredo, nunca no appsettings.</summary>
    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}

public enum SmtpSecurity
{
    None,
    Auto,
    StartTls,
    SslOnConnect
}
