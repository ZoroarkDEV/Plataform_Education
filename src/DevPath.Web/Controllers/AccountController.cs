using DevPath.Application.Auth;
using DevPath.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Cadastro, login e logout. Controller fino: valida o formulário, chama o
/// <see cref="IAuthService"/> (camada Application) e decide qual tela mostrar.
/// </summary>
[AllowAnonymous]
public sealed class AccountController(IAuthService authService) : Controller
{
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

        return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
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

    // Erros do serviço vão para o ModelState sem campo (string.Empty) e aparecem no asp-validation-summary.
    private void AddErrors(AuthResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
