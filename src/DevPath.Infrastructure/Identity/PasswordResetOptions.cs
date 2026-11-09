namespace DevPath.Infrastructure.Identity;

/// <summary>Seção <c>App</c>: dados públicos da aplicação.</summary>
public sealed class AppUrlOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// URL pública da Web (ex.: https://devpath.app, http://localhost:8080). Os links dos e-mails são
    /// montados a partir dela, NUNCA do header Host da requisição (que o cliente controla: um atacante
    /// pediria a recuperação da conta da vítima com Host: site-dele e receberia o token).
    /// Sem ela, o e-mail de recuperação não é enviado (erro no log).
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>Seção <c>Identity</c>: tempos do Identity que dependem do ambiente.</summary>
public sealed class IdentityTimingOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Validade do link de nova senha (e dos outros tokens do DataProtectorTokenProvider).
    /// US-AUTH.1: 1 hora. Formato TimeSpan: <c>01:00:00</c>.
    /// </summary>
    public TimeSpan PasswordResetTokenLifespan { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// De quanto em quanto tempo o cookie de login confere o security stamp no banco. Depois de trocar a
    /// senha, as outras sessões caem em até este tempo (o padrão do Identity seria 30 min).
    /// </summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(1);
}
