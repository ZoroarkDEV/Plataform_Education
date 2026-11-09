using System.Text;
using DevPath.Application.Auth;
using DevPath.Application.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.Identity;

/// <summary>
/// Recuperação de senha com os tokens do Identity (issue #44, US-AUTH.1).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Expira:</b> <c>DataProtectionTokenProviderOptions.TokenLifespan</c> =
/// <c>Identity:PasswordResetTokenLifespan</c> (1 h).</item>
/// <item><b>Uma vez só:</b> o token leva o security stamp do usuário; o <c>ResetPasswordAsync</c> troca
/// o stamp junto com a senha, então o mesmo link (e qualquer outro pedido antes dele) deixa de valer.</item>
/// <item><b>Sessões antigas:</b> o mesmo stamp novo derruba os outros cookies de login no próximo
/// <c>SecurityStampValidator</c> (<c>Identity:SecurityStampValidationInterval</c>).</item>
/// <item><b>Sem enumeração:</b> <see cref="RequestResetAsync"/> só enfileira; quem busca o usuário e
/// envia é o <see cref="PasswordResetEmailWorker"/>.</item>
/// </list>
/// </remarks>
public sealed class PasswordResetService(
    UserManager<ApplicationUser> userManager,
    PasswordResetQueue queue,
    IEmailSender emailSender,
    IOptions<AppUrlOptions> appUrl,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    /// <summary>Rota da tela de nova senha na Web (AccountController.ResetPassword).</summary>
    public const string ResetPagePath = "/Account/ResetPassword";

    public const string GenericFailureMessage = "Não foi possível alterar a senha. Tente de novo.";

    public ValueTask RequestResetAsync(string email, CancellationToken cancellationToken = default)
    {
        queue.Enqueue(email.Trim());
        return ValueTask.CompletedTask;
    }

    /// <summary>Chamado pelo worker: busca a conta e, se existir, envia o link.</summary>
    internal async Task SendResetEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || string.IsNullOrEmpty(user.Email))
        {
            logger.LogInformation("Recuperação de senha pedida para um e-mail sem conta: nada enviado.");
            return;
        }

        if (!TryGetBaseUrl(out var baseUrl))
        {
            logger.LogError(
                "E-mail de recuperação de senha NÃO enviado: App:PublicBaseUrl ausente ou inválida (precisa ser http(s) absoluta).");
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = BuildLink(baseUrl, user.Id, token);

        await emailSender.SendAsync(
            PasswordResetEmail.Build(user.Email, user.DisplayName, link, tokenOptions.Value.TokenLifespan),
            cancellationToken);

        logger.LogInformation("E-mail de recuperação de senha enviado para o usuário {UserId}.", user.Id);
    }

    public async Task<bool> IsLinkValidAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        var (user, decodedToken) = await ResolveAsync(userId, token);
        return user is not null && await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
            decodedToken!);
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var (user, decodedToken) = await ResolveAsync(request.UserId, request.Token);
        if (user is null)
        {
            return ResetPasswordResult.LinkNoLongerValid();
        }

        // Confere o token (validade + stamp), valida a política de senha, grava o hash e TROCA o security
        // stamp, tudo no mesmo UpdateAsync. O stamp novo invalida este link e as outras sessões.
        var result = await userManager.ResetPasswordAsync(user, decodedToken!, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return ResetPasswordResult.LinkNoLongerValid();
            }

            return ResetPasswordResult.Failure(result.Errors
                .Select(e => e.Code.StartsWith("Password", StringComparison.Ordinal)
                    ? PortugueseIdentityErrorDescriber.PasswordRule
                    : GenericFailureMessage)
                .Distinct());
        }

        // Quem provou que tem acesso ao e-mail sai do bloqueio por senhas erradas (US-AUTH.1: "eu entro").
        if (await userManager.GetAccessFailedCountAsync(user) > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        logger.LogInformation("Senha redefinida pelo link de recuperação para o usuário {UserId}.", user.Id);
        return ResetPasswordResult.Success();
    }

    /// <summary>Link absoluto: <c>{App:PublicBaseUrl}/Account/ResetPassword?userId=...&amp;token=...</c>.</summary>
    public static string BuildLink(Uri baseUrl, Guid userId, string token)
    {
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return QueryHelpers.AddQueryString(
            baseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/') + ResetPagePath,
            new Dictionary<string, string?> { ["userId"] = userId.ToString(), ["token"] = encodedToken });
    }

    private bool TryGetBaseUrl(out Uri baseUrl)
    {
        var configured = appUrl.Value.PublicBaseUrl?.Trim();
        if (Uri.TryCreate(configured, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            baseUrl = parsed;
            return true;
        }

        baseUrl = null!;
        return false;
    }

    private async Task<(ApplicationUser? User, string? Token)> ResolveAsync(string userId, string token)
    {
        if (!Guid.TryParse(userId, out var id) || string.IsNullOrWhiteSpace(token))
        {
            return (null, null);
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return (null, null);
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        return (user, user is null ? null : decoded);
    }
}
