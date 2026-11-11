using System.Text.Json;
using System.Text.RegularExpressions;
using DevPath.Application.Auth;
using DevPath.Application.Lessons;
using DevPath.Web.Lessons;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Onboarding;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Tela de leitura da aula (#12) + conclusão (#27) + refresh pós-Executar (#30).
/// [Authorize]: sem login, redireciona para /Account/Login?ReturnUrl=...
/// <para>
/// Acesso por progresso (#27, ADR-007): depois do conteúdo, <see cref="ILessonProgressGateway.OpenAsync"/>
/// decide e registra a abertura. Ordem: inexistente &gt; rascunho &gt; acesso &gt; defeito (HTTP 503, view Unavailable).
/// Bloqueada e de outra trilha = 403 com Unavailable; sem trilha = /escolha-sua-trilha; conta apagada = sai e vai ao login.
/// Nunca 500 fora de Development. O estado vai para <see cref="LessonPageViewModel.Progress"/>.
/// </para>
/// </summary>
[Authorize]
public sealed partial class LessonsController(
    ILessonContentService lessons,
    LessonMarkdownRenderer renderer,
    ILessonProgressGateway progress,
    IAuthService auth,
    IModuleCompletionReader modules,
    IStudentAchievementSource achievements,
    IHostEnvironment environment,
    ILogger<LessonsController> logger) : Controller
{
    public const string UnavailableView = "Unavailable";
    public const string FlashKey = "LessonFlash";
    public const string CompletionPartial = "_LessonCompletion";
    public const string PagerPartial = "_LessonPager";
    public const string ModuleCompletedView = "ModuleCompleted";

    [HttpGet("/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}")]
    public async Task<IActionResult> Show(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct)
    {
        pathSlug = pathSlug.ToLowerInvariant();
        moduleSlug = moduleSlug.ToLowerInvariant();
        lessonSlug = lessonSlug.ToLowerInvariant();

        if (!IsSlug(pathSlug) || !IsSlug(moduleSlug) || !IsSlug(lessonSlug))
        {
            logger.LogInformation("Aula não encontrada: endereço inválido {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.NotFound, "Slug inválido no endereço.");
        }

        LessonLoadResult result;
        try
        {
            result = await lessons.GetLessonAsync(pathSlug, moduleSlug, lessonSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha inesperada ao carregar a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        // Ordem (ADR-007): inexistente/rascunho (404) > acesso (403) > defeito. Aula bloqueada com defeito
        // responde 403 e não revela o defeito.
        if (result.Error is { Kind: LessonLoadErrorKind.NotFound or LessonLoadErrorKind.Draft } notFound)
        {
            logger.LogInformation("Aula indisponível ({Kind}) {Path}/{Module}/{Lesson}: {Detail}", notFound.Kind, pathSlug, moduleSlug, lessonSlug, notFound.Detail);
            return Unavailable(notFound.Kind == LessonLoadErrorKind.Draft ? LessonUnavailableReason.Draft : LessonUnavailableReason.NotFound, notFound.Detail);
        }

        // Acesso (#27): antes do defeito e antes de renderizar.
        if (!User.TryGetUserId(out var userId))
            return RedirectToAction(nameof(AccountController.Login), "Account");

        LessonAccessOutcome access;
        try
        {
            access = await progress.OpenAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao abrir progresso da aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            // Sem progresso não dá para saber se a aula está liberada: não abre (página amigável, 503 fora de Dev).
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        var accessGate = await GateAccessAsync(access, ct);
        if (accessGate is not null) return accessGate;

        if (result.Error is { } error)
        {
            logger.LogError("Aula com defeito {Path}/{Module}/{Lesson}: {Detail}", pathSlug, moduleSlug, lessonSlug, error.Detail);
            return Unavailable(LessonUnavailableReason.Malformed, error.Detail);
        }

        var lesson = result.Lesson;
        if (lesson is null || string.IsNullOrWhiteSpace(lesson.MarkdownBody))
        {
            logger.LogError("Aula com defeito {Path}/{Module}/{Lesson}: resultado sem aula ou com corpo vazio.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, "Resultado sem aula ou MarkdownBody vazio.");
        }

        RenderedLesson html;
        LessonExerciseViewModel? exercise;
        try
        {
            html = renderer.Render(lesson.MarkdownBody);
            exercise = lesson.Exercise is null ? null : LessonExerciseViewModel.From(lesson.Exercise, renderer);
            if (exercise is not null && access.State?.Exercise is { } exProgress)
                exercise = exercise.WithProgress(exProgress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao montar a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        var flash = TempData[FlashKey] as string;
        ViewData["Title"] = lesson.Title;
        return View(new LessonPageViewModel(lesson, html, exercise, access.State, flash));
    }

    /// <summary>
    /// POST concluir (HTMX ou PRG). Idempotente.
    /// <paramref name="skipWithoutPoints"/> = formulário de "Seguir sem pontos" → <see cref="ILessonProgressGateway.SkipAsync"/>;
    /// caso contrário → <see cref="ILessonProgressGateway.CompleteAsync"/> (nunca grava skip).
    /// </summary>
    [HttpPost("/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}/concluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        string pathSlug, string moduleSlug, string lessonSlug,
        bool skipWithoutPoints = false,
        CancellationToken ct = default)
    {
        pathSlug = pathSlug.ToLowerInvariant();
        moduleSlug = moduleSlug.ToLowerInvariant();
        lessonSlug = lessonSlug.ToLowerInvariant();

        if (!IsSlug(pathSlug) || !IsSlug(moduleSlug) || !IsSlug(lessonSlug))
            return NotFound();

        if (!User.TryGetUserId(out var userId))
            return RedirectToAction(nameof(AccountController.Login), "Account");

        LessonCompletionOutcome outcome;
        try
        {
            outcome = skipWithoutPoints
                ? await progress.SkipAsync(userId, pathSlug, moduleSlug, lessonSlug, ct)
                : await progress.CompleteAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao concluir a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            if (IsHtmx)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Content(LessonCompletionTexts.SaveError, "text/plain; charset=utf-8");
            }
            TempData[FlashKey] = LessonCompletionTexts.SaveError;
            return RedirectToAction(nameof(Show), new { pathSlug, moduleSlug, lessonSlug });
        }

        if (outcome.Status == LessonCompletionStatus.UserNotFound)
        {
            await auth.LogoutAsync();
            if (IsHtmx)
            {
                Response.Headers["HX-Redirect"] = "/Account/Login";
                return Ok();
            }
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        if (outcome.Status is LessonCompletionStatus.Locked or LessonCompletionStatus.NotEnrolled or LessonCompletionStatus.LessonNotFound)
        {
            var lessonUrl = Url.Action(nameof(Show), new { pathSlug, moduleSlug, lessonSlug })!;
            if (IsHtmx)
            {
                Response.Headers["HX-Redirect"] = lessonUrl;
                return Ok();
            }
            return Redirect(lessonUrl);
        }

        if (outcome.Status == LessonCompletionStatus.Unavailable)
        {
            // Progresso ainda não disponível: não há o que concluir.
            return RedirectToAction(nameof(Show), new { pathSlug, moduleSlug, lessonSlug });
        }

        // Precisa do LessonDto para montar a partial (breadcrumb). Carrega de novo (barato / fake nos testes).
        var load = await lessons.GetLessonAsync(pathSlug, moduleSlug, lessonSlug, ct);
        if (load.Lesson is null || outcome.State is null)
            return RedirectToAction(nameof(Show), new { pathSlug, moduleSlug, lessonSlug });

        var justSkipped = skipWithoutPoints && outcome.Status == LessonCompletionStatus.Completed;
        var justCompleted = outcome.Status == LessonCompletionStatus.Completed;
        var completion = LessonCompletionViewModel.From(outcome.State, load.Lesson, justCompleted: justCompleted, justSkipped: justSkipped);
        // #42: concluiu agora a última aula pronta do módulo? (AlreadyCompleted = refazer: não mostra de novo.)
        var moduleDone = justCompleted ? await CompletedModuleAsync(userId, pathSlug, moduleSlug, ct) : null;
        completion = completion with { ModuleCompleted = moduleDone };
        var page = new LessonPageViewModel(load.Lesson, renderer.Render(load.Lesson.MarkdownBody), Progress: outcome.State);

        if (IsHtmx)
        {
            if (outcome.Status == LessonCompletionStatus.Completed)
            {
                Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new
                {
                    lessonCompleted = new { progressPercentage = outcome.ProgressPercentage }
                });
            }

            ViewData["PagerLabel"] = "Navegação entre aulas (fim da página)";
            ViewData["PagerOob"] = true;
            ViewData["LessonPage"] = page;
            return PartialView(CompletionPartial, completion);
        }

        // PRG sem HTMX. Módulo concluído agora: vai para a tela "Módulo concluído" (#42).
        if (moduleDone is not null)
            return Redirect(moduleDone.SummaryUrl);

        if (outcome.Status == LessonCompletionStatus.Completed)
            TempData[FlashKey] = completion.SuccessMessage;
        else if (outcome.Status == LessonCompletionStatus.RequirementNotMet)
            TempData[FlashKey] = LessonCompletionTexts.RequirementNotMetFlash;

        return RedirectToAction(nameof(Show), new { pathSlug, moduleSlug, lessonSlug });
    }

    /// <summary>GET partial de conclusão + pager OOB (depois de um pass com lessonCompleted).</summary>
    [HttpGet("/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}/conclusao")]
    public async Task<IActionResult> Conclusao(string pathSlug, string moduleSlug, string lessonSlug,
        [FromQuery(Name = "antes")] string? previousKind = null, CancellationToken ct = default)
    {
        pathSlug = pathSlug.ToLowerInvariant();
        moduleSlug = moduleSlug.ToLowerInvariant();
        lessonSlug = lessonSlug.ToLowerInvariant();

        if (!IsSlug(pathSlug) || !IsSlug(moduleSlug) || !IsSlug(lessonSlug))
            return NotFound();

        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var access = await progress.GetStateAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        if (access.Status is not LessonAccessStatus.Allowed || access.State is null)
            return NotFound();

        var load = await lessons.GetLessonAsync(pathSlug, moduleSlug, lessonSlug, ct);
        if (load.Lesson is null)
            return NotFound();

        // Depois de um pass: só anuncia, sem data-just-completed (não move o foco; aula já concluída não parece nova).
        LessonCompletionKind? before = Enum.TryParse<LessonCompletionKind>(previousKind, ignoreCase: false, out var k)
            && Enum.IsDefined(k) ? k : null;
        var completion = access.State.IsCompleted
            ? LessonCompletionViewModel.AfterPass(access.State, load.Lesson, before)
            : LessonCompletionViewModel.From(access.State, load.Lesson);
        // #42: o pass concluiu agora (antes = sem conclusão) a última aula pronta do módulo.
        if (access.State.IsCompleted && before is null)
            completion = completion with { ModuleCompleted = await CompletedModuleAsync(userId, pathSlug, moduleSlug, ct) };
        // #45: aviso de selo só neste pass (JustPassed). O gancho fica vazio até o Backend expor
        // ExecutionOutcome.NewlyUnlockedAchievement; o adapter preenche o mesmo formato do DTO.
        if (completion.JustPassed)
        {
            var unlocked = await achievements.GetNewlyUnlockedAsync(userId, ct);
            if (unlocked is not null)
                completion = completion with { JustUnlocked = unlocked };
        }
        ViewData["PagerOob"] = true;
        // Page model for OOB pager (rendered inside completion partial footer).
        ViewData["LessonPage"] = new LessonPageViewModel(load.Lesson, renderer.Render(load.Lesson.MarkdownBody), Progress: access.State);
        return PartialView(CompletionPartial, completion);
    }

    /// <summary>
    /// Tela "Módulo concluído" (#42, US-M.1): aberta pelo PRG da conclusão e pelo "Concluído ✓" do dashboard.
    /// Sem trilha = escolha de trilha; módulo de outra trilha ou inexistente = 404 amigável; ainda não concluído =
    /// dashboard. Falha = 503 amigável (nunca 500).
    /// </summary>
    [HttpGet("/trilhas/{pathSlug}/modulos/{moduleSlug}/concluido")]
    public async Task<IActionResult> ModuleCompleted(string pathSlug, string moduleSlug, CancellationToken ct)
    {
        pathSlug = pathSlug.ToLowerInvariant();
        moduleSlug = moduleSlug.ToLowerInvariant();
        if (!IsSlug(pathSlug) || !IsSlug(moduleSlug))
            return Unavailable(LessonUnavailableReason.NotFound, "Slug inválido no endereço.");

        if (!User.TryGetUserId(out var userId))
            return RedirectToAction(nameof(AccountController.Login), "Account");

        ModuleCompletionLookup lookup;
        try
        {
            lookup = await modules.LookupAsync(userId, pathSlug, moduleSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao montar o resumo do módulo {Path}/{Module}.", pathSlug, moduleSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        switch (lookup.Status)
        {
            case ModuleCompletionLookupStatus.NoPath:
                return RedirectToAction(nameof(OnboardingController.ChoosePath), "Onboarding");
            case ModuleCompletionLookupStatus.NotCompleted:
                return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
            case ModuleCompletionLookupStatus.Completed when lookup.Module is not null:
                break;
            default:
                return Unavailable(LessonUnavailableReason.NotFound, "Módulo fora da trilha atual ou inexistente.");
        }

        var model = lookup.Module!;
        ViewData["Title"] = model.Title;
        return View(ModuleCompletedView, model);
    }

    // Só para o bloco/tela do #42: se falhar, a conclusão continua valendo e a tela cai na mensagem normal.
    private async Task<ModuleCompletionViewModel?> CompletedModuleAsync(Guid userId, string pathSlug, string moduleSlug, CancellationToken ct)
    {
        try
        {
            var lookup = await modules.LookupAsync(userId, pathSlug, moduleSlug, ct);
            return lookup.Status == ModuleCompletionLookupStatus.Completed ? lookup.Module : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao ver se o módulo {Path}/{Module} foi concluído.", pathSlug, moduleSlug);
            return null;
        }
    }

    private async Task<IActionResult?> GateAccessAsync(LessonAccessOutcome access, CancellationToken ct)
    {
        switch (access.Status)
        {
            case LessonAccessStatus.Unavailable:
            case LessonAccessStatus.Allowed:
                return null;

            case LessonAccessStatus.UserNotFound:
                await auth.LogoutAsync();
                return RedirectToAction(nameof(AccountController.Login), "Account");

            case LessonAccessStatus.LessonNotFound:
                return Unavailable(LessonUnavailableReason.NotFound, "Progresso: LessonNotFound.");

            case LessonAccessStatus.NotEnrolled when access.CurrentPathTitle is null:
                return RedirectToAction(nameof(OnboardingController.ChoosePath), "Onboarding");

            case LessonAccessStatus.NotEnrolled:
                return Unavailable(LessonUnavailableReason.OtherPath, "OtherPath", currentPathTitle: access.CurrentPathTitle);

            case LessonAccessStatus.Locked:
            {
                var req = access.State?.UnlockRequirement;
                string? url = req?.Url;
                var canOpen = false;
                if (req is not null && User.TryGetUserId(out var uid))
                {
                    var reqAccess = await progress.GetStateAsync(uid, req.PathSlug, req.ModuleSlug, req.LessonSlug, ct);
                    canOpen = reqAccess.Status == LessonAccessStatus.Allowed;
                }
                return Unavailable(
                    LessonUnavailableReason.Locked,
                    "Locked",
                    requiredTitle: req?.Title,
                    requiredUrl: url,
                    requiredCanOpen: canOpen);
            }

            default:
                return Unavailable(LessonUnavailableReason.NotFound, $"Status {access.Status}");
        }
    }

    private ViewResult Unavailable(
        LessonUnavailableReason reason,
        string detail,
        string? requiredTitle = null,
        string? requiredUrl = null,
        bool requiredCanOpen = false,
        string? currentPathTitle = null)
    {
        // Malformed = 503 + Unavailable (Backend #27): nunca 500, nem em Development.
        Response.StatusCode = reason switch
        {
            LessonUnavailableReason.Malformed => StatusCodes.Status503ServiceUnavailable,
            LessonUnavailableReason.Locked or LessonUnavailableReason.OtherPath => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status404NotFound,
        };

        var model = new LessonUnavailableViewModel(
            reason,
            environment.IsDevelopment() ? detail : null,
            requiredTitle,
            requiredUrl,
            requiredCanOpen,
            currentPathTitle);
        ViewData["Title"] = model.Title;
        return View(UnavailableView, model);
    }

    private bool IsHtmx =>
        string.Equals(Request.Headers["HX-Request"], "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsSlug(string? value) => value is { Length: > 0 and <= 200 } && SlugRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
