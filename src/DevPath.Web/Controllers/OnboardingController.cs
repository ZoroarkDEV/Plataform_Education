using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>"Escolha sua trilha" (US-11.2). Exige login: sem cookie, vai para o login com ReturnUrl.</summary>
[Authorize]
[Route("escolha-sua-trilha")]
public sealed class OnboardingController(IStudentJourneyGateway journey) : Controller
{
    public const string FlashKey = "Flash";
    private const string ErrorKey = "OnboardingError";

    [HttpGet("")]
    public async Task<IActionResult> ChoosePath(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Challenge();
        }

        // Já escolheu? Vai direto ao dashboard (uma trilha por vez; trocar fica para outra issue).
        if (await journey.HasCurrentPathAsync(userId, cancellationToken))
        {
            return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
        }

        var paths = await journey.GetActivePathsAsync(cancellationToken);
        return View(new ChoosePathViewModel(paths, TempData[ErrorKey] as string));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChoosePath([FromForm] string? pathSlug, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(pathSlug))
        {
            TempData[ErrorKey] = "Escolha uma das trilhas abaixo para começar.";
            return RedirectToAction(nameof(ChoosePath));
        }

        var outcome = await journey.EnrollAsync(userId, pathSlug.Trim(), cancellationToken);
        switch (outcome.Status)
        {
            case EnrollmentStatus.Enrolled:
                TempData[FlashKey] = $"Você começou a trilha {outcome.PathTitle}!";
                return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
            case EnrollmentStatus.AlreadyEnrolled:
                return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
            case EnrollmentStatus.PathNotFound:
                TempData[ErrorKey] = "Não encontramos essa trilha. Escolha uma das opções abaixo.";
                return RedirectToAction(nameof(ChoosePath));
            default:
                TempData[ErrorKey] = "Não conseguimos salvar sua escolha agora. Tente de novo em alguns minutos.";
                return RedirectToAction(nameof(ChoosePath));
        }
    }
}
