namespace DevPath.Infrastructure.Email;

/// <summary>Seção <c>Email</c> do appsettings / variáveis <c>Email__*</c>.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Remetente (ex.: nao-responda@devpath.app). Precisa ser aceito pelo provedor SMTP.</summary>
    public string FromAddress { get; set; } = "nao-responda@devpath.local";

    public string FromName { get; set; } = "DevPath";

    /// <summary>
    /// Quem envia: <c>Smtp</c> (dev/staging: Mailpit), <c>Resend</c> (produção, decisão do dono em #44)
    /// ou <c>None</c> (não envia, só avisa no log). Vazio = <c>Smtp</c> se <c>Email:Smtp:Host</c> existir,
    /// senão <c>None</c>.
    /// </summary>
    public string? Provider { get; set; }

    public SmtpOptions Smtp { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();
}

/// <summary>
/// Seção <c>Email:Resend</c>. A chave (<see cref="ApiKey"/>) vem SÓ do ambiente/secret:
/// <c>Email__Resend__ApiKey</c> ou <c>RESEND_API_KEY</c>. Nunca no appsettings versionado.
/// </summary>
public sealed class ResendOptions
{
    /// <summary>Nome da variável de ambiente alternativa (a que a documentação do Resend usa).</summary>
    public const string ApiKeyEnvironmentVariable = "RESEND_API_KEY";

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.resend.com";

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>Valores aceitos em <c>Email:Provider</c>.</summary>
public static class EmailProviders
{
    public const string Smtp = "Smtp";
    public const string Resend = "Resend";
    public const string None = "None";
}

/// <summary>O provedor recusou ou não respondeu. O worker registra e segue para o próximo e-mail.</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

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
