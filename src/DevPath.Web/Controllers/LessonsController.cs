using System.Text.RegularExpressions;
using DevPath.Application.Auth;
using DevPath.Application.Journey;
using DevPath.Application.Lessons;
using DevPath.Application.Progress;
using DevPath.Web.Lessons;
using DevPath.Web.Onboarding;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Tela de leitura da aula (#12). Fino de propósito: busca a aula no <see cref="ILessonContentService"/>,
/// converte o Markdown e escolhe a view. Nenhum texto de aula mora aqui (ADR-003).
/// [Authorize]: sem login, o cookie middleware redireciona para /Account/Login?ReturnUrl=... (decisão de produto).
/// <para>
/// Acesso por progresso (#27, ADR-007), o mínimo no servidor: depois do conteúdo, <see cref="ILessonProgressService.OpenAsync"/>
/// decide e registra a abertura. Ordem: inexistente &gt; rascunho &gt; acesso &gt; defeito. Bloqueada e de outra trilha =
/// 403 com a página Unavailable; sem trilha = /escolha-sua-trilha; conta apagada = sai e vai ao login.
/// O estado vai para <see cref="LessonPageViewModel.Progress"/>: é a costura para o bloco de conclusão e a paginação do UI.
/// </para>
/// </summary>
[Authorize]
public sealed partial class LessonsController(
    ILessonContentService lessons,
    ILessonProgressService progress,
    IAuthService auth,
    LessonMarkdownRenderer renderer,
    IHostEnvironment environment,
    ILogger<LessonsController> logger) : Controller
{
    public const string UnavailableView = "Unavailable";

    [HttpGet("/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}")]
    public async Task<IActionResult> Show(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct)
    {
        // O serviço do Backend compara em minúsculas; normalizamos antes de validar o formato.
        pathSlug = pathSlug.ToLowerInvariant();
        moduleSlug = moduleSlug.ToLowerInvariant();
        lessonSlug = lessonSlug.ToLowerInvariant();

        if (!IsSlug(pathSlug) || !IsSlug(moduleSlug) || !IsSlug(lessonSlug))
        {
            logger.LogInformation("Aula não encontrada: endereço inválido {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.NotFound, "Slug inválido no endereço.");
        }

        LessonLoadResult result;
        LessonProgressState? progressState = null;
        try
        {
            result = await lessons.GetLessonAsync(pathSlug, moduleSlug, lessonSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O contrato diz "sem exceções"; se mesmo assim vier uma, o aluno vê a página amigável.
            logger.LogError(ex, "Falha inesperada ao carregar a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        // Rascunho e inexistente respondem antes do acesso (404); defeito só depois (não revela aula bloqueada).
        if (result.Error is not { Kind: LessonLoadErrorKind.NotFound or LessonLoadErrorKind.Draft })
        {
            var denied = await CheckAccessAsync(pathSlug, moduleSlug, lessonSlug, ct);
            if (denied.Result is not null)
            {
                return denied.Result;
            }

            progressState = denied.State;
        }

        if (result.Error is { } error)
        {
            var reason = error.Kind switch
            {
                LessonLoadErrorKind.Draft => LessonUnavailableReason.Draft,
                LessonLoadErrorKind.Malformed => LessonUnavailableReason.Malformed,
                _ => LessonUnavailableReason.NotFound,
            };

            if (reason == LessonUnavailableReason.Malformed)
                logger.LogError("Aula com defeito {Path}/{Module}/{Lesson}: {Detail}", pathSlug, moduleSlug, lessonSlug, error.Detail);
            else
                logger.LogInformation("Aula indisponível ({Kind}) {Path}/{Module}/{Lesson}: {Detail}", error.Kind, pathSlug, moduleSlug, lessonSlug, error.Detail);

            return Unavailable(reason, error.Detail);
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
            // Exercício vem do próprio LessonDto (visão segura: sem solução, casos de teste nem erros comuns).
            exercise = lesson.Exercise is null ? null : LessonExerciseViewModel.From(lesson.Exercise, renderer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao montar a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        ViewData["Title"] = lesson.Title;
        return View(new LessonPageViewModel(lesson, html, exercise, progressState));
    }

    /// <summary>Result != null = negado (já com a resposta pronta); senão, o estado do aluno na aula.</summary>
    private async Task<(IActionResult? Result, LessonProgressState? State)> CheckAccessAsync(
        string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return (Challenge(), null);
        }

        var access = await progress.OpenAsync(userId, pathSlug, moduleSlug, lessonSlug, ct);
        switch (access.Kind)
        {
            case LessonAccessKind.Allowed:
                return (null, access.State);
            case LessonAccessKind.Locked:
                logger.LogInformation("Aula bloqueada {Path}/{Module}/{Lesson} para {UserId}.", pathSlug, moduleSlug, lessonSlug, userId);
                return (Unavailable(LessonUnavailableReason.Locked, "Aula bloqueada pelo progresso (R1).",
                    unlockRequirement: access.State?.UnlockRequirement), null);
            case LessonAccessKind.NotEnrolled when access.CurrentPathTitle is null:
                // Sem trilha: primeiro escolhe uma (o mesmo destino do dashboard).
                return (RedirectToAction(nameof(OnboardingController.ChoosePath), "Onboarding"), null);
            case LessonAccessKind.NotEnrolled:
                logger.LogInformation("Aula de outra trilha {Path}/{Module}/{Lesson} para {UserId}.", pathSlug, moduleSlug, lessonSlug, userId);
                return (Unavailable(LessonUnavailableReason.OtherPath, "Aula fora da trilha atual.",
                    currentPathTitle: access.CurrentPathTitle), null);
            case LessonAccessKind.UserNotFound:
                // O cookie aponta para uma conta que não existe mais: encerra a sessão e pede login de novo.
                await auth.LogoutAsync();
                return (RedirectToAction(nameof(AccountController.Login), "Account"), null);
            default:
                logger.LogInformation("Aula não encontrada pelo progresso {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
                return (Unavailable(LessonUnavailableReason.NotFound, "Aula não encontrada pelo serviço de progresso."), null);
        }
    }

    private ViewResult Unavailable(LessonUnavailableReason reason, string detail,
        LessonRefDto? unlockRequirement = null, string? currentPathTitle = null)
    {
        Response.StatusCode = reason switch
        {
            LessonUnavailableReason.Malformed => StatusCodes.Status500InternalServerError,
            LessonUnavailableReason.Locked or LessonUnavailableReason.OtherPath => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status404NotFound,
        };

        var model = new LessonUnavailableViewModel(reason, environment.IsDevelopment() ? detail : null,
            unlockRequirement, currentPathTitle);
        ViewData["Title"] = model.Title;
        return View(UnavailableView, model);
    }

    private static bool IsSlug(string? value) => value is { Length: > 0 and <= 200 } && SlugRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
