using System.Text.RegularExpressions;
using DevPath.Application.Lessons;
using DevPath.Web.Lessons;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>
/// Tela de leitura da aula (#12). Fino de propósito: busca a aula no <see cref="ILessonContentService"/>,
/// converte o Markdown e escolhe a view. Nenhum texto de aula mora aqui (ADR-003).
/// [Authorize]: sem login, o cookie middleware redireciona para /Account/Login?ReturnUrl=... (decisão de produto).
/// </summary>
[Authorize]
public sealed partial class LessonsController(
    ILessonContentService lessons,
    LessonMarkdownRenderer renderer,
    ILessonExerciseSource exercises,
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
            var studentExercise = await exercises.GetForLessonAsync(lesson.LessonId, ct);
            exercise = studentExercise is null ? null : LessonExerciseViewModel.From(studentExercise, renderer);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao montar a aula {Path}/{Module}/{Lesson}.", pathSlug, moduleSlug, lessonSlug);
            return Unavailable(LessonUnavailableReason.Malformed, ex.Message);
        }

        ViewData["Title"] = lesson.Title;
        return View(new LessonPageViewModel(lesson, html, exercise));
    }

    private ViewResult Unavailable(LessonUnavailableReason reason, string detail)
    {
        Response.StatusCode = reason == LessonUnavailableReason.Malformed
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status404NotFound;

        var model = new LessonUnavailableViewModel(reason, environment.IsDevelopment() ? detail : null);
        ViewData["Title"] = model.Title;
        return View(UnavailableView, model);
    }

    private static bool IsSlug(string? value) => value is { Length: > 0 and <= 200 } && SlugRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
