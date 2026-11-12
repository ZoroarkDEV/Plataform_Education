namespace DevPath.Application.Email;

/// <summary>Quem envia os e-mails do DevPath (#44/#47).</summary>
public enum EmailProviderKind
{
    /// <summary>Não envia nada (só um aviso no log).</summary>
    None,

    /// <summary>SMTP do servidor (<c>Email:Smtp:*</c>). Dev e staging: Mailpit.</summary>
    Smtp,

    /// <summary>API do Resend (produção).</summary>
    Resend
}

/// <summary>De onde vem a configuração que o envio usa AGORA.</summary>
public enum EmailSettingsSource
{
    /// <summary>appsettings/variáveis de ambiente (<c>Email__*</c>): nada salvo pela tela, ou ambiente travado.</summary>
    Configuration,

    /// <summary>O que o admin salvou na tela (<c>/admin</c>), por cima da configuração.</summary>
    Database
}

/// <summary>De onde vem a chave do Resend.</summary>
public enum EmailSecretSource
{
    None,

    /// <summary>Salva pela tela, protegida com ASP.NET Data Protection.</summary>
    Database,

    /// <summary><c>Email__Resend__ApiKey</c> ou <c>RESEND_API_KEY</c> (secret do deploy).</summary>
    Environment
}

/// <summary>
/// O que a tela de admin mostra (#47). NUNCA traz a chave do Resend: só se existe e os 4 últimos caracteres.
/// </summary>
/// <param name="Provider">Provedor salvo pela tela; sem nada salvo, o da configuração.</param>
/// <param name="FromAddress">Remetente salvo (ou o da configuração).</param>
/// <param name="FromName">Nome do remetente salvo (ou o da configuração).</param>
/// <param name="ResendKeyConfigured">Há chave do Resend legível (a salva pela tela ou, sem ela, a do ambiente).</param>
/// <param name="ResendKeyHint">Os 4 últimos caracteres da chave (<c>null</c> sem chave ou se ela for curta demais).</param>
/// <param name="ResendKeySource">De onde vem a chave do Resend.</param>
/// <param name="RestartRequired">
/// <c>true</c> quando algo em <paramref name="MissingRequirements"/> só se resolve na configuração do servidor
/// (variável de ambiente + reinício), ex.: <c>Smtp</c> sem <c>Email:Smtp:Host</c>. O que a tela salva vale sem reiniciar.
/// </param>
/// <param name="MissingRequirements">O que falta para o envio funcionar (PT-BR, pronto para a tela). Vazio = pode enviar.</param>
/// <param name="UpdatedAt">Última alteração pela tela (<c>null</c> = nunca salvo).</param>
/// <param name="UpdatedByUserId">Quem salvou por último.</param>
/// <param name="UpdatedByDisplayName">Nome de quem salvou por último (<c>null</c> se a conta foi apagada).</param>
/// <param name="EffectiveSource">De onde vem o que o envio usa agora.</param>
/// <param name="LockedToConfiguration">
/// <c>true</c> em dev e staging (<c>Email:LockedToConfiguration</c>): o envio segue a configuração do servidor
/// (Mailpit) e o que a tela salvar só vale onde o ambiente não é travado (produção). A tela deve avisar.
/// </param>
/// <param name="EffectiveProvider">Provedor que o envio usa agora.</param>
/// <param name="EffectiveFromAddress">Remetente que o envio usa agora.</param>
/// <param name="CanSend"><c>true</c> se o envio está pronto (provedor diferente de None e nada faltando).</param>
public sealed record EmailSettingsDto(
    EmailProviderKind Provider,
    string FromAddress,
    string FromName,
    bool ResendKeyConfigured,
    string? ResendKeyHint,
    EmailSecretSource ResendKeySource,
    bool RestartRequired,
    IReadOnlyList<string> MissingRequirements,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedByUserId,
    string? UpdatedByDisplayName,
    EmailSettingsSource EffectiveSource,
    bool LockedToConfiguration,
    EmailProviderKind EffectiveProvider,
    string EffectiveFromAddress,
    bool CanSend);

/// <summary>Formulário da tela. A chave só entra quando o admin digita uma nova.</summary>
/// <param name="Provider">None, Smtp ou Resend.</param>
/// <param name="FromAddress">Obrigatório (e-mail válido) quando o provedor não é None.</param>
/// <param name="FromName">Opcional (vazio = "DevPath"), até 100 caracteres, sem quebra de linha.</param>
/// <param name="NewResendApiKey">Chave nova. Vazio/null = mantém a salva.</param>
/// <param name="ClearResendApiKey"><c>true</c> = apaga a chave salva (não pode vir junto com uma chave nova).</param>
public sealed record UpdateEmailSettingsCommand(
    EmailProviderKind Provider,
    string? FromAddress,
    string? FromName,
    string? NewResendApiKey = null,
    bool ClearResendApiKey = false)
{
    /// <summary>Nunca imprime a chave (o record geraria ToString com ela).</summary>
    public override string ToString() =>
        $"UpdateEmailSettingsCommand {{ Provider = {Provider}, FromAddress = {FromAddress}, FromName = {FromName}, " +
        $"NewResendApiKey = {(string.IsNullOrEmpty(NewResendApiKey) ? "(vazio)" : "(informada)")}, ClearResendApiKey = {ClearResendApiKey} }}";
}

/// <summary>Resultado do salvar.</summary>
/// <param name="Status">Saved, Invalid ou Forbidden.</param>
/// <param name="Errors">
/// Erros por campo, com o NOME da propriedade do <see cref="UpdateEmailSettingsCommand"/> como chave
/// (ex.: <c>FromAddress</c>), prontos para o ModelState. Chave vazia = erro geral.
/// </param>
/// <param name="Settings">Como ficou (só quando <paramref name="Status"/> = Saved).</param>
public sealed record EmailSettingsSaveResult(
    EmailSettingsSaveStatus Status,
    IReadOnlyDictionary<string, string[]> Errors,
    EmailSettingsDto? Settings)
{
    public bool Succeeded => Status == EmailSettingsSaveStatus.Saved;
}

public enum EmailSettingsSaveStatus
{
    Saved,
    Invalid,

    /// <summary>O usuário não tem o papel Admin (a Web deve responder 404, como em /admin/tentativas).</summary>
    Forbidden
}

public enum EmailTestStatus
{
    /// <summary>O provedor aceitou o e-mail.</summary>
    Sent,

    /// <summary>Falta configuração (ver <see cref="EmailSettingsDto.MissingRequirements"/>) ou o provedor é None.</summary>
    NotConfigured,

    /// <summary>O provedor recusou ou não respondeu (domínio não verificado, chave inválida, timeout...).</summary>
    Failed,

    /// <summary>Pediu de novo cedo demais (<see cref="EmailTestResult.RetryAfter"/>).</summary>
    RateLimited,

    /// <summary>Não é admin.</summary>
    Forbidden
}

/// <summary>Resultado do "Enviar e-mail de teste" (para o e-mail do admin logado).</summary>
/// <param name="Status">O que aconteceu.</param>
/// <param name="Message">Mensagem PT-BR pronta para a tela. Nunca tem a chave.</param>
/// <param name="SentTo">E-mail do admin (destino), quando conhecido.</param>
/// <param name="Provider">Provedor usado (ou que seria usado).</param>
/// <param name="RetryAfter">Quando <see cref="EmailTestStatus.RateLimited"/>: quanto esperar.</param>
public sealed record EmailTestResult(
    EmailTestStatus Status,
    string Message,
    string? SentTo,
    EmailProviderKind Provider,
    TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Status == EmailTestStatus.Sent;
}

/// <summary>
/// Configuração de e-mail da tela de admin (#47). Só para o papel Admin: cada método confere o papel do
/// <c>adminUserId</c> (defesa em profundidade; a Web também usa a policy <c>Admin</c>).
/// </summary>
public interface IEmailSettingsService
{
    /// <summary>Configuração salva + o que o envio usa agora e o que falta. Nunca devolve a chave.</summary>
    Task<EmailSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Valida e salva; vale no próximo envio, sem reiniciar. Registra quem e quando, sem segredo.</summary>
    Task<EmailSettingsSaveResult> SaveAsync(
        UpdateEmailSettingsCommand command, Guid adminUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia um e-mail de teste para o e-mail do admin, com a configuração em vigor. Limite: 1 por minuto por admin.</summary>
    Task<EmailTestResult> SendTestAsync(Guid adminUserId, CancellationToken cancellationToken = default);
}
