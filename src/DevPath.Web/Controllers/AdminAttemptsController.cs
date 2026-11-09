using DevPath.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Relatório de tentativas do Executar para o dono (#43): GET /admin/tentativas.
/// Sem login: vai para o login (como toda página [Authorize]). Logado sem o papel "Admin": 404, para a área
/// administrativa nem aparecer para alunos. Como dar o papel: docs/analytics/tentativas.md.
/// </summary>
[Authorize]
[Route("admin/tentativas")]
public sealed class AdminAttemptsController(IAuthorizationService authorization, IAttemptAnalyticsService analytics) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var allowed = await authorization.AuthorizeAsync(User, AdminAccess.Policy);
        if (!allowed.Succeeded)
        {
            return NotFound();
        }

        // Dado agregado de vários alunos: não vai para cache de navegador/proxy.
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Admin/Tentativas.cshtml", await analytics.GetReportAsync(cancellationToken));
    }
}
