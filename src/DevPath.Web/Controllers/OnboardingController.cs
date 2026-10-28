using DevPath.Application.Auth;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>"Escolha sua trilha" (US-11.2). Exige login: sem cookie, vai para o login com ReturnUrl.</summary>
[Authorize]
[Route("escolha-sua-trilha")]
public sealed class OnboardingController(IStudentJourneyGateway journey, IAuthService auth) : Controller
{
    public const string FlashKey = "Flash";
    private const string ErrorKey = "OnboardingError";

    public const string AlreadyEnrolledMessage =
        "Você já está em uma trilha. Por enquanto é uma trilha por vez: continue de onde parou.";

    /// <summary>Aviso do AlreadyEnrolled com o título da trilha atual (genérico se o título não vier).</summary>
    public static string AlreadyEnrolledMessageFor(string? currentPathTitle) =>
        currentPathTitle is { Length: > 0 } title
            ? $"Você já está na trilha {title}. Por enquanto é uma trilha por vez: continue de onde parou."
            : AlreadyEnrolledMessage;

    public const string NoLessonsMessage =
        "Essa trilha ainda não tem aulas. Escolha outra trilha ou volte em breve.";

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
                TempData[FlashKey] = outcome.PathTitle is { Length: > 0 } title
                    ? $"Você começou a trilha {title}!"
                    : "Você começou sua trilha!";
                return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
            case EnrollmentStatus.AlreadyEnrolled:
                // Uma trilha por vez (SPEC §5.1): não troca; também é o 2º POST de um duplo clique.
                TempData[FlashKey] = AlreadyEnrolledMessageFor(outcome.PathTitle);
                return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
            case EnrollmentStatus.PathNotFound:
                TempData[ErrorKey] = "Não encontramos essa trilha. Escolha uma das opções abaixo.";
                return RedirectToAction(nameof(ChoosePath));
            case EnrollmentStatus.NoLessons:
                TempData[ErrorKey] = NoLessonsMessage;
                return RedirectToAction(nameof(ChoosePath));
            case EnrollmentStatus.UserNotFound:
                // O cookie aponta para uma conta que não existe mais: encerra a sessão e pede login de novo.
                await auth.LogoutAsync();
                return RedirectToAction(nameof(AccountController.Login), "Account");
            default:
                TempData[ErrorKey] = "Não conseguimos salvar sua escolha agora. Tente de novo em alguns minutos.";
                return RedirectToAction(nameof(ChoosePath));
        }
    }
}
