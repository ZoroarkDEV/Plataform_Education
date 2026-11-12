using System.Net.Mail;
using System.Security.Cryptography;
using DevPath.Application.Email;
using DevPath.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// Protege a chave do Resend com o ASP.NET Data Protection (AES-256-CBC + HMACSHA256, chaves com rotação).
/// O texto protegido vai para o banco; o anel de chaves fica FORA do banco (volume <c>web_keys</c> ou
/// <c>DataProtection:KeysPath</c>), então um backup do banco sozinho não revela a chave.
/// </summary>
public sealed class ResendApiKeyProtector(IDataProtectionProvider provider)
{
    /// <summary>Propósito do protector: separa este segredo dos cookies e tokens do Identity.</summary>
    public const string Purpose = "DevPath.Email.ResendApiKey.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string apiKey) => _protector.Protect(apiKey);

    /// <summary><c>null</c> se o texto não puder ser aberto (anel de chaves perdido/trocado ou valor adulterado).</summary>
    public string? TryUnprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>
/// A configuração que vale para UM envio (#47), já resolvida. <see cref="ToString"/> nunca mostra a chave.
/// </summary>
public sealed class EffectiveEmailSettings
{
    public required string Provider { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }
    public required EmailSettingsSource Source { get; init; }
    public required bool LockedToConfiguration { get; init; }

    /// <summary>Chave do Resend do envio. Segredo: nunca em log, DTO ou mensagem.</summary>
    internal string? ResendApiKey { get; init; }

    /// <summary>Chave "de produção" (a salva pela tela; sem ela, a do ambiente), independente da trava. Segredo.</summary>
    internal string? ConfiguredResendApiKey { get; init; }

    public required EmailSecretSource ConfiguredResendKeySource { get; init; }

    /// <summary>Há chave salva, mas o Data Protection não consegue abri-la.</summary>
    public required bool StoredKeyUnreadable { get; init; }

    /// <summary>O que o admin salvou (sem a chave aberta). <c>null</c> = nada salvo.</summary>
    public StoredEmailSettings? Stored { get; init; }

    public required IReadOnlyList<string> MissingRequirements { get; init; }

    public required bool RestartRequired { get; init; }

    public bool CanSend => Provider != EmailProviders.None && MissingRequirements.Count == 0;

    internal EmailTransport ToTransport() => new(FromAddress, FromName, ResendApiKey);

    public override string ToString() =>
        $"EffectiveEmailSettings {{ Provider = {Provider}, FromAddress = {FromAddress}, Source = {Source}, CanSend = {CanSend} }}";
}

/// <summary>O que está salvo na tabela, sem segredo.</summary>
public sealed record StoredEmailSettings(
    string Provider, string FromAddress, string FromName, bool HasResendKey, DateTimeOffset UpdatedAt, Guid? UpdatedByUserId);

/// <summary>
/// Resolve, a cada envio, qual provedor/remetente/chave valem (#47):
/// <list type="number">
/// <item><c>Email:LockedToConfiguration=true</c> (Development e compose de dev/staging): só a configuração do servidor (Mailpit).</item>
/// <item>Senão, se o admin salvou algo na tela: o que foi salvo vale por cima da configuração. A chave do Resend
/// salva vale por cima da do ambiente; sem chave salva, usa <c>Email__Resend__ApiKey</c>/<c>RESEND_API_KEY</c>.</item>
/// <item>Sem nada salvo: a configuração (<c>Email:*</c>), como no #44.</item>
/// </list>
/// O servidor SMTP (host, porta, TLS, usuário/senha) vem sempre da configuração.
/// Cache curto por instância (<see cref="EmailOptions.SettingsCacheSeconds"/>); salvar chama <see cref="Invalidate"/>.
/// </summary>
public sealed class EmailSettingsResolver(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    ResendApiKeyProtector protector,
    TimeProvider time,
    ILogger<EmailSettingsResolver> logger)
{
    private static readonly TimeSpan DatabaseErrorCacheDuration = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CacheEntry? _cache;
    private long _version;

    /// <summary>Esquece o cache: o próximo envio relê o banco. Chamado depois de salvar.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        Volatile.Write(ref _cache, null);
    }

    public async Task<EffectiveEmailSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = Volatile.Read(ref _cache);
        if (cached is not null && cached.ExpiresAt > time.GetUtcNow())
        {
            return cached.Settings;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            cached = Volatile.Read(ref _cache);
            if (cached is not null && cached.ExpiresAt > time.GetUtcNow())
            {
                return cached.Settings;
            }

            var version = Interlocked.Read(ref _version);

            var (record, readFailed) = await ReadStoredAsync(cancellationToken);
            var settings = Resolve(options.Value, record, protector);
            var duration = readFailed
                ? DatabaseErrorCacheDuration
                : TimeSpan.FromSeconds(Math.Max(0, options.Value.SettingsCacheSeconds));
            if (Interlocked.Read(ref _version) == version)
            {
                // Um "salvar" no meio da leitura invalida: não guarda o valor velho.
                Volatile.Write(ref _cache, new CacheEntry(settings, time.GetUtcNow() + duration));
            }

            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(EmailSettingsRecord? Record, bool Failed)> ReadStoredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<DevPathDbContext>();
        if (db is null)
        {
            return (null, false); // sem banco registrado (ex.: testes só de DI): só a configuração
        }

        try
        {
            return (await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora do ar ou migration não aplicada: segue com a configuração do servidor.
            logger.LogWarning("Configuração de e-mail salva não pôde ser lida ({Error}); usando a do servidor.", ex.GetType().Name);
            return (null, true);
        }
    }

    /// <summary>Regra de precedência + o que falta. Pura (sem I/O): testável sem banco.</summary>
    public static EffectiveEmailSettings Resolve(EmailOptions configuration, EmailSettingsRecord? record, ResendApiKeyProtector protector)
    {
        var configuredProvider = EmailProviders.Normalize(configuration.Provider) ?? EmailProviders.None;
        var environmentKey = string.IsNullOrWhiteSpace(configuration.Resend.ApiKey) ? null : configuration.Resend.ApiKey.Trim();

        string? storedKey = null;
        var storedKeyUnreadable = false;
        if (record?.ResendApiKeyProtected is { Length: > 0 } protectedKey)
        {
            storedKey = protector.TryUnprotect(protectedKey);
            storedKeyUnreadable = storedKey is null;
        }

        // A chave "de produção": a salva pela tela; sem chave salva, a do ambiente.
        var (configuredKey, keySource) = record?.ResendApiKeyProtected is { Length: > 0 }
            ? (storedKey, EmailSecretSource.Database)
            : (environmentKey, environmentKey is null ? EmailSecretSource.None : EmailSecretSource.Environment);

        var useStored = record is not null && !configuration.LockedToConfiguration;
        var provider = useStored ? EmailProviders.Normalize(record!.Provider) ?? EmailProviders.None : configuredProvider;
        var fromAddress = (useStored ? record!.FromAddress : configuration.FromAddress)?.Trim() ?? string.Empty;
        var fromName = (useStored ? record!.FromName : configuration.FromName)?.Trim();
        if (string.IsNullOrEmpty(fromName))
        {
            fromName = DefaultFromName;
        }

        var effectiveKey = useStored ? configuredKey : environmentKey;
        var effectiveKeyUnreadable = useStored && storedKeyUnreadable;

        var missing = new List<string>();
        var restartRequired = false;
        switch (provider)
        {
            case EmailProviders.None:
                missing.Add("O provedor está em None: nenhum e-mail é enviado. Escolha Smtp ou Resend.");
                break;
            case EmailProviders.Smtp when string.IsNullOrWhiteSpace(configuration.Smtp.Host):
                missing.Add("O servidor não tem SMTP configurado (Email__Smtp__Host). Isso se ajusta no ambiente do servidor e exige reiniciar a Web.");
                restartRequired = true;
                break;
            case EmailProviders.Resend when effectiveKeyUnreadable:
                missing.Add("A chave do Resend salva não pôde ser lida (as chaves de proteção do servidor mudaram). Cadastre a chave de novo.");
                break;
            case EmailProviders.Resend when string.IsNullOrWhiteSpace(effectiveKey):
                missing.Add(useStored || !configuration.LockedToConfiguration
                    ? "Falta a chave da API do Resend. Cadastre a chave nesta tela."
                    : "Falta a chave da API do Resend (Email__Resend__ApiKey ou RESEND_API_KEY no servidor).");
                break;
        }

        if (provider != EmailProviders.None && !IsValidEmailAddress(fromAddress))
        {
            missing.Add(string.IsNullOrEmpty(fromAddress)
                ? "Falta o e-mail do remetente (FromAddress)."
                : "O e-mail do remetente (FromAddress) não é válido.");
        }

        return new EffectiveEmailSettings
        {
            Provider = provider,
            FromAddress = fromAddress,
            FromName = fromName,
            Source = useStored ? EmailSettingsSource.Database : EmailSettingsSource.Configuration,
            LockedToConfiguration = configuration.LockedToConfiguration,
            ResendApiKey = provider == EmailProviders.Resend ? effectiveKey : null,
            ConfiguredResendApiKey = configuredKey,
            ConfiguredResendKeySource = keySource,
            StoredKeyUnreadable = storedKeyUnreadable,
            Stored = record is null
                ? null
                : new StoredEmailSettings(
                    EmailProviders.Normalize(record.Provider) ?? EmailProviders.None, record.FromAddress, record.FromName,
                    record.ResendApiKeyProtected is { Length: > 0 }, record.UpdatedAt, record.UpdatedByUserId),
            MissingRequirements = missing,
            RestartRequired = restartRequired
        };
    }

    public const string DefaultFromName = "DevPath";

    private sealed record CacheEntry(EffectiveEmailSettings Settings, DateTimeOffset ExpiresAt);

    /// <summary>Um endereço só, sem nome de exibição nem espaços (ex.: <c>nao-responda@devpath.app</c>).</summary>
    public static bool IsValidEmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            return false;
        }

        return MailAddress.TryCreate(value, out var address)
               && address.Address == value
               && string.IsNullOrEmpty(address.DisplayName)
               && address.Host.Contains('.');
    }
}
