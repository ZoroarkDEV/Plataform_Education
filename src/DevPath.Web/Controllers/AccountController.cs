using DevPath.Application.Auth;
using DevPath.Web.Auth;
using DevPath.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Cadastro, login, logout e recuperação de senha (#44). Controller fino: valida o formulário, chama o
/// <see cref="IAuthService"/> (camada Application) e decide qual tela mostrar.
/// </summary>
[AllowAnonymous]
public sealed class AccountController(
    IAuthService authService,
    IPasswordResetService passwordResetService,
    PasswordResetRateLimiter passwordResetRateLimiter) : Controller
{
    /// <summary>US-AUTH.1: a mesma frase exista a conta ou não.</summary>
    public const string ForgotPasswordSentMessage =
        "Se este e-mail tiver uma conta, enviamos um link para criar uma senha nova. Confira também o spam.";

    public const string PasswordChangedMessage = "Senha alterada. Você já pode entrar.";

    public const string LinkNoLongerValidMessage = "Este link não vale mais. Peça um novo.";

    public const string TooManyRequestsMessage =
        "Você fez muitos pedidos seguidos. Espere alguns minutos e tente de novo.";

    /// <summary>Chave do TempData com a mensagem de sucesso que a tela Entrar mostra uma vez.</summary>
    public const string LoginStatusTempDataKey = "LoginStatus";

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await authService.RegisterAsync(
            new RegisterRequest(model.Email, model.Password, model.DisplayName), cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // US-11.1: conta criada e já autenticada, vai direto para "Escolha sua trilha".
        return RedirectToAction(nameof(OnboardingController.ChoosePath), "Onboarding");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await authService.LoginAsync(
            new LoginRequest(model.Email, model.Password, model.RememberMe), cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // Url.IsLocalUrl evita "open redirect": ?returnUrl=https://site-malicioso.com é ignorado.
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await authService.LogoutAsync();
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ----- Recuperação de senha (#44, US-AUTH.1) -----

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        // Por IP primeiro: conta todo POST, válido ou não. A recusa não diz nada sobre a conta.
        if (!passwordResetRateLimiter.TryAcquireForIp(HttpContext.Connection.RemoteIpAddress))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = "600";
            ModelState.AddModelError(string.Empty, TooManyRequestsMessage);
            return View(model);
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Por e-mail: passou do limite, a resposta é a MESMA (só não enfileira outro e-mail).
        if (passwordResetRateLimiter.TryAcquireForEmail(model.Email))
        {
            // Só enfileira: buscar a conta e enviar acontece em segundo plano, então o tempo de resposta
            // é igual exista a conta ou não (ADR-005).
            await passwordResetService.RequestResetAsync(model.Email, cancellationToken);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? userId, string? token, CancellationToken cancellationToken)
    {
        NoReferrerNoStore();
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)
            || !await passwordResetService.IsLinkValidAsync(userId, token, cancellationToken))
        {
            return View("ResetPasswordInvalid");
        }

        return View(new ResetPasswordViewModel { UserId = userId, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        NoReferrerNoStore();
        if (string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.Token))
        {
            return View("ResetPasswordInvalid");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await passwordResetService.ResetPasswordAsync(
            new ResetPasswordRequest(model.UserId, model.Token, model.Password), cancellationToken);

        if (result.InvalidLink)
        {
            return View("ResetPasswordInvalid");
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(ResetPasswordViewModel.Password), error);
            }

            return View(model);
        }

        // Este navegador também começa do zero: a sessão (se houver) é de antes da troca.
        await authService.LogoutAsync();
        TempData[LoginStatusTempDataKey] = PasswordChangedMessage;
        return RedirectToAction(nameof(Login));
    }

    // O token está na URL: não pode vazar no Referer de links da página nem ficar em cache.
    private void NoReferrerNoStore()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "no-store";
    }

    // Erros do serviço vão para o ModelState sem campo (string.Empty) e aparecem no asp-validation-summary.
    private void AddErrors(AuthResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
