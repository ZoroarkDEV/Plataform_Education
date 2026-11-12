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

    /// <summary>
    /// <c>true</c> = o envio usa SÓ esta configuração e ignora o que o admin salvou na tela (#47).
    /// Ligado em Development e no docker-compose (dev e staging continuam no Mailpit); desligado em produção.
    /// </summary>
    public bool LockedToConfiguration { get; set; }

    /// <summary>Por quanto tempo a configuração salva na tela fica em cache por instância (salvar limpa na hora).</summary>
    public int SettingsCacheSeconds { get; set; } = 30;

    /// <summary>Intervalo mínimo entre dois "Enviar e-mail de teste" do mesmo admin.</summary>
    public int TestEmailCooldownSeconds { get; set; } = 60;

    public SmtpOptions Smtp { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();
}

/// <summary>
/// Seção <c>Email:Resend</c>. A chave (<see cref="ApiKey"/>) vem do ambiente/secret
/// (<c>Email__Resend__ApiKey</c> ou <c>RESEND_API_KEY</c>) ou da tela de admin (#47, protegida no banco).
/// Nunca no appsettings versionado.
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

    /// <summary>Nome canônico (<c>Smtp</c>, <c>Resend</c>, <c>None</c>) ou <c>null</c> se desconhecido.</summary>
    public static string? Normalize(string? value) => value?.Trim() switch
    {
        { } v when v.Equals(Smtp, StringComparison.OrdinalIgnoreCase) => Smtp,
        { } v when v.Equals(Resend, StringComparison.OrdinalIgnoreCase) => Resend,
        { } v when v.Equals(None, StringComparison.OrdinalIgnoreCase) => None,
        _ => null
    };

    public static DevPath.Application.Email.EmailProviderKind ToKind(string provider) => provider switch
    {
        Smtp => DevPath.Application.Email.EmailProviderKind.Smtp,
        Resend => DevPath.Application.Email.EmailProviderKind.Resend,
        _ => DevPath.Application.Email.EmailProviderKind.None
    };

    public static string FromKind(DevPath.Application.Email.EmailProviderKind kind) => kind switch
    {
        DevPath.Application.Email.EmailProviderKind.Smtp => Smtp,
        DevPath.Application.Email.EmailProviderKind.Resend => Resend,
        _ => None
    };
}

/// <summary>O provedor recusou ou não respondeu. O worker registra e segue para o próximo e-mail.</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null, int? statusCode = null)
    : Exception(message, inner)
{
    /// <summary>Status HTTP do provedor, quando ele respondeu (ex.: 401, 403, 422).</summary>
    public int? StatusCode { get; } = statusCode;
}

/// <summary>
/// Remetente e chave de UM envio, já resolvidos (configuração do servidor ou tela de admin).
/// <see cref="ToString"/> nunca mostra a chave.
/// </summary>
internal sealed record EmailTransport(string FromAddress, string FromName, string? ResendApiKey)
{
    public override string ToString() => $"EmailTransport {{ FromAddress = {FromAddress}, FromName = {FromName} }}";
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
