using DevPath.Application.Analytics;
using DevPath.Application.Email;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Email;

/// <summary>Tela de admin das configurações de e-mail (#47). Veja docs/configuracoes-de-email.md.</summary>
public sealed class EmailSettingsService(
    DevPathDbContext db,
    UserManager<ApplicationUser> users,
    EmailSettingsResolver resolver,
    ResendApiKeyProtector protector,
    IEmailSender sender,
    TestEmailThrottle throttle,
    TimeProvider time,
    ILogger<EmailSettingsService> logger) : IEmailSettingsService
{
    public const int FromNameMaxLength = 100;
    public const int ResendApiKeyMaxLength = 200;
    public const string ResendApiKeyPrefix = "re_";
    public const string TestEmailSubject = "DevPath: e-mail de teste";

    /// <summary>Só mostra os 4 últimos caracteres de chaves com pelo menos este tamanho.</summary>
    private const int MinLengthForHint = 12;

    public async Task<EmailSettingsDto> GetAsync(CancellationToken cancellationToken = default) =>
        await ToDtoAsync(await resolver.GetAsync(cancellationToken), cancellationToken);

    public async Task<EmailSettingsSaveResult> SaveAsync(
        UpdateEmailSettingsCommand command, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await IsAdminAsync(adminUserId))
        {
            return new EmailSettingsSaveResult(EmailSettingsSaveStatus.Forbidden,
                new Dictionary<string, string[]> { [string.Empty] = ["Só administradores alteram a configuração de e-mail."] }, null);
        }

        var errors = Validate(command, out var fromAddress, out var fromName, out var newKey);
        if (errors.Count > 0)
        {
            return new EmailSettingsSaveResult(EmailSettingsSaveStatus.Invalid,
                errors.ToDictionary(e => e.Key, e => e.Value.ToArray()), null);
        }

        var record = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            record = new EmailSettingsRecord();
            db.EmailSettings.Add(record);
        }

        record.Provider = EmailProviders.FromKind(command.Provider);
        record.FromAddress = fromAddress;
        record.FromName = fromName;
        var keyChange = "mantida";
        if (newKey is not null)
        {
            record.ResendApiKeyProtected = protector.Protect(newKey);
            keyChange = "trocada";
        }
        else if (command.ClearResendApiKey)
        {
            keyChange = record.ResendApiKeyProtected is null ? "ausente" : "removida";
            record.ResendApiKeyProtected = null;
        }
        else if (record.ResendApiKeyProtected is null)
        {
            keyChange = "ausente";
        }

        record.UpdatedAt = time.GetUtcNow();
        record.UpdatedByUserId = adminUserId;
        await db.SaveChangesAsync(cancellationToken);
        resolver.Invalidate();

        // Auditoria sem segredo: quem, provedor e o que aconteceu com a chave (nunca o valor).
        logger.LogInformation(
            "Configuração de e-mail salva pelo admin {UserId}: provedor {Provider}, chave do Resend {KeyChange}.",
            adminUserId, record.Provider, keyChange);

        return new EmailSettingsSaveResult(EmailSettingsSaveStatus.Saved, new Dictionary<string, string[]>(),
            await GetAsync(cancellationToken));
    }

    public async Task<EmailTestResult> SendTestAsync(Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var admin = await FindAdminAsync(adminUserId);
        if (admin is null)
        {
            return new EmailTestResult(EmailTestStatus.Forbidden, "Só administradores enviam o e-mail de teste.", null, EmailProviderKind.None);
        }

        var settings = await resolver.GetAsync(cancellationToken);
        var provider = EmailProviders.ToKind(settings.Provider);
        var to = admin.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            return new EmailTestResult(EmailTestStatus.NotConfigured, "Sua conta não tem e-mail para receber o teste.", null, provider);
        }

        if (!settings.CanSend)
        {
            return new EmailTestResult(EmailTestStatus.NotConfigured,
                "O envio não está pronto. " + string.Join(" ", settings.MissingRequirements), to, provider);
        }

        if (throttle.TryAcquire(adminUserId) is { } retryAfter)
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            return new EmailTestResult(EmailTestStatus.RateLimited,
                $"Aguarde {seconds} s para enviar outro e-mail de teste.", to, provider, retryAfter);
        }

        var message = BuildTestMessage(to, settings);
        try
        {
            await sender.SendAsync(message, cancellationToken);
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogWarning("E-mail de teste não entregue ({Provider}): {Reason}", settings.Provider, ex.Message);
            return new EmailTestResult(EmailTestStatus.Failed, DescribeFailure(settings.Provider, ex), to, provider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // SMTP fora do ar, TLS, autenticação... A mensagem da exceção não tem segredo (a senha do SMTP não entra nela).
            logger.LogWarning("E-mail de teste não entregue ({Provider}): {Error}", settings.Provider, ex.GetType().Name);
            return new EmailTestResult(EmailTestStatus.Failed,
                $"Não foi possível enviar pelo {settings.Provider} ({ex.GetType().Name}). Confira o servidor de e-mail.", to, provider);
        }

        logger.LogInformation("E-mail de teste enviado pelo admin {UserId} via {Provider}.", adminUserId, settings.Provider);
        return new EmailTestResult(EmailTestStatus.Sent,
            $"E-mail de teste enviado para {to} via {settings.Provider}. Confira a caixa de entrada (e o spam).", to, provider);
    }

    private static string DescribeFailure(string provider, EmailDeliveryException ex) => ex.StatusCode switch
    {
        401 or 403 when provider == EmailProviders.Resend =>
            $"O Resend recusou (HTTP {ex.StatusCode}): confira a chave da API e se o domínio do remetente está verificado no painel do Resend.",
        422 when provider == EmailProviders.Resend =>
            "O Resend recusou (HTTP 422): o remetente precisa ser de um domínio verificado no painel do Resend.",
        429 => "O provedor limitou os envios (HTTP 429). Tente de novo em alguns minutos.",
        _ => $"O envio falhou: {ex.Message}"
    };

    private static EmailMessage BuildTestMessage(string to, EffectiveEmailSettings settings)
    {
        var text = $"""
            Este é um e-mail de teste do DevPath.

            Se ele chegou, o envio está funcionando (provedor {settings.Provider}, remetente {settings.FromAddress}).
            """;
        var html = $"""
            <p>Este é um e-mail de teste do DevPath.</p>
            <p>Se ele chegou, o envio está funcionando (provedor <strong>{settings.Provider}</strong>,
            remetente <strong>{System.Net.WebUtility.HtmlEncode(settings.FromAddress)}</strong>).</p>
            """;
        return new EmailMessage(to, TestEmailSubject, html, text);
    }

    private static Dictionary<string, List<string>> Validate(
        UpdateEmailSettingsCommand command, out string fromAddress, out string fromName, out string? newKey)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        if (!Enum.IsDefined(command.Provider))
        {
            Add(nameof(command.Provider), "Escolha None, Smtp ou Resend.");
        }

        fromAddress = command.FromAddress?.Trim() ?? string.Empty;
        if (command.Provider != EmailProviderKind.None && fromAddress.Length == 0)
        {
            Add(nameof(command.FromAddress), "Informe o e-mail do remetente.");
        }
        else if (fromAddress.Length > 0 && !EmailSettingsResolver.IsValidEmailAddress(fromAddress))
        {
            Add(nameof(command.FromAddress), "Informe um e-mail válido, ex.: nao-responda@seudominio.com.");
        }

        fromName = command.FromName?.Trim() ?? string.Empty;
        if (fromName.Length == 0)
        {
            fromName = EmailSettingsResolver.DefaultFromName;
        }
        else if (fromName.Length > FromNameMaxLength)
        {
            Add(nameof(command.FromName), $"Use no máximo {FromNameMaxLength} caracteres.");
        }
        else if (fromName.Any(c => char.IsControl(c) || c is '<' or '>' or '"'))
        {
            Add(nameof(command.FromName), "O nome não pode ter quebra de linha nem os caracteres < > \".");
        }

        newKey = string.IsNullOrWhiteSpace(command.NewResendApiKey) ? null : command.NewResendApiKey.Trim();
        if (newKey is not null)
        {
            if (command.ClearResendApiKey)
            {
                Add(nameof(command.NewResendApiKey), "Escolha entre informar uma chave nova ou remover a chave salva.");
            }
            else if (!newKey.StartsWith(ResendApiKeyPrefix, StringComparison.Ordinal) || newKey.Length < 10
                     || newKey.Length > ResendApiKeyMaxLength || newKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                // Sem ecoar o valor digitado.
                Add(nameof(command.NewResendApiKey), "Isso não parece uma chave do Resend (começa com \"re_\"). Copie a chave de novo no painel do Resend.");
            }
        }

        return errors;
    }

    private async Task<EmailSettingsDto> ToDtoAsync(EffectiveEmailSettings settings, CancellationToken cancellationToken)
    {
        var stored = settings.Stored;
        string? updatedBy = null;
        if (stored?.UpdatedByUserId is { } userId)
        {
            updatedBy = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var key = settings.ConfiguredResendApiKey;
        return new EmailSettingsDto(
            Provider: EmailProviders.ToKind(stored?.Provider ?? settings.Provider),
            FromAddress: stored?.FromAddress ?? settings.FromAddress,
            FromName: stored is null ? settings.FromName : (stored.FromName.Length == 0 ? EmailSettingsResolver.DefaultFromName : stored.FromName),
            ResendKeyConfigured: !string.IsNullOrEmpty(key),
            ResendKeyHint: key is { Length: >= MinLengthForHint } ? key[^4..] : null,
            ResendKeySource: settings.ConfiguredResendKeySource,
            RestartRequired: settings.RestartRequired,
            MissingRequirements: settings.MissingRequirements,
            UpdatedAt: stored?.UpdatedAt,
            UpdatedByUserId: stored?.UpdatedByUserId,
            UpdatedByDisplayName: updatedBy,
            EffectiveSource: settings.Source,
            LockedToConfiguration: settings.LockedToConfiguration,
            EffectiveProvider: EmailProviders.ToKind(settings.Provider),
            EffectiveFromAddress: settings.FromAddress,
            CanSend: settings.CanSend);
    }

    private async Task<bool> IsAdminAsync(Guid userId) => await FindAdminAsync(userId) is not null;

    private async Task<ApplicationUser?> FindAdminAsync(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var user = await users.FindByIdAsync(userId.ToString());
        return user is not null && await users.IsInRoleAsync(user, AdminAccess.Role) ? user : null;
    }
}
