using DevPath.Application.Auth;
using DevPath.Web.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Dashboard do aluno (US-11.3). [Authorize]: sem login, o cookie middleware redireciona para /Account/Login.
/// Sem trilha escolhida, manda para "Escolha sua trilha".
/// </summary>
[Authorize]
public sealed class DashboardController(IStudentJourneyGateway journey) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Challenge();
        }

        var dashboard = await journey.GetDashboardAsync(userId, cancellationToken);
        if (dashboard is null)
        {
            return RedirectToAction(nameof(OnboardingController.ChoosePath), "Onboarding");
        }

        ViewData["DisplayName"] = User.FindFirst(DevPathClaimTypes.DisplayName)?.Value ?? User.Identity?.Name;
        ViewData[OnboardingController.FlashKey] = TempData[OnboardingController.FlashKey] as string;
        return View(dashboard);
    }
}
