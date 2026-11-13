using DevPath.Application.Analytics;
using DevPath.Application.Email;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Configuração de e-mail pelo admin (#47): GET /admin/email, POST /admin/email (salvar) e
/// POST /admin/email/teste (e-mail de teste). Mesmo padrão do /admin/tentativas: sem login vai para o login;
/// logado sem o papel "Admin" recebe 404. Sem cache, antiforgery nos POSTs e PRG com mensagem de sucesso.
/// A chave do Resend só entra no POST: nunca volta no HTML, no ModelState, no TempData nem no redirect.
/// Contrato: docs/configuracoes-de-email.md.
/// </summary>
[Authorize]
[Route("admin/email")]
public sealed class AdminEmailController(IAuthorizationService authorization, IEmailSettingsService emailSettings) : Controller
{
    public const string ViewPath = "~/Views/Admin/Email.cshtml";
    public const string SavedTempDataKey = "AdminEmailSaved";
    public const string TestStatusTempDataKey = "AdminEmailTestStatus";
    public const string TestTitleTempDataKey = "AdminEmailTestTitle";
    public const string TestDetailTempDataKey = "AdminEmailTestDetail";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync())
        {
            return NotFound();
        }

        NoStore();
        var settings = await emailSettings.GetAsync(cancellationToken);
        return View(ViewPath, EmailSettingsPageViewModel.From(settings,
            savedMessage: TempData[SavedTempDataKey] as string,
            testNotice: ReadTestNotice()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] EmailSettingsForm form, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync() || !User.TryGetUserId(out var userId))
        {
            return NotFound();
        }

        NoStore();
        var command = form.ToCommand();

        // O valor digitado não fica em lugar nenhum da resposta (nem como "attempted value" do ModelState).
        ModelState.Remove(nameof(EmailSettingsForm.NewResendApiKey));
        form.NewResendApiKey = null;

        var result = await emailSettings.SaveAsync(command, userId, cancellationToken);
        switch (result.Status)
        {
            case EmailSettingsSaveStatus.Saved:
                TempData[SavedTempDataKey] = EmailSettingsTexts.Saved;
                return RedirectToAction(nameof(Index));
            case EmailSettingsSaveStatus.Forbidden:
                // O contrato pede 404, como o GET (o papel pode ter sido removido depois do login).
                return NotFound();
        }

        foreach (var (field, messages) in result.Errors)
        {
            var key = EmailSettingsForm.FieldNames.Contains(field) ? field : string.Empty;
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        if (ModelState.ErrorCount == 0)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível salvar. Confira os campos e tente de novo.");
        }

        var settings = await emailSettings.GetAsync(cancellationToken);
        return View(ViewPath, EmailSettingsPageViewModel.From(settings, form));
    }

    [HttpPost("teste")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync() || !User.TryGetUserId(out var userId))
        {
            return NotFound();
        }

        var notice = EmailSettingsTexts.ToNotice(await emailSettings.SendTestAsync(userId, cancellationToken));
        TempData[TestStatusTempDataKey] = notice.Status.ToString();
        TempData[TestTitleTempDataKey] = notice.Title;
        TempData[TestDetailTempDataKey] = notice.Detail;
        return RedirectToAction(nameof(Index), null, null, "email-teste");
    }

    private EmailTestNotice? ReadTestNotice() =>
        TempData[TestStatusTempDataKey] is string status && Enum.TryParse<EmailTestStatus>(status, out var kind)
            && TempData[TestTitleTempDataKey] is string title
            ? new EmailTestNotice(kind, title, TempData[TestDetailTempDataKey] as string)
            : null;

    private async Task<bool> IsAdminAsync() => (await authorization.AuthorizeAsync(User, AdminAccess.Policy)).Succeeded;

    // Configuração do servidor e dados de auditoria: não vão para cache de navegador/proxy.
    private void NoStore() => Response.Headers.CacheControl = "no-store";
}
