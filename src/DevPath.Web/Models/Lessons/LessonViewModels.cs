using DevPath.Application.Lessons;
using DevPath.Domain.Common;
using DevPath.Web.Lessons;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Models.Lessons;

/// <summary>Tudo que a tela da aula precisa: dados + HTML + progresso (#27) + exercício (#30).</summary>
public sealed record LessonPageViewModel(
    LessonDto Lesson,
    RenderedLesson Html,
    LessonExerciseViewModel? Exercise = null,
    LessonProgressView? Progress = null,
    string? FlashMessage = null)
{
    public string LevelLabel => LevelLabels.For(Lesson.Level);

    public string EstimatedTimeLabel => $"~{Lesson.EstimatedMinutes} min";

    public string PositionLabel => Lesson is { Position: > 0, TotalInModule: > 0 }
        ? $"Aula {Lesson.Position} de {Lesson.TotalInModule}"
        : "Aula";

    /// <summary>Progresso disponível (serviço na dev). Pending = null → UI como antes do #27.</summary>
    public bool HasProgress => Progress is not null;

    public bool IsCompleted => Progress?.IsCompleted == true;

    public LessonCompletionViewModel? Completion =>
        Progress is null ? null : LessonCompletionViewModel.From(Progress, Lesson, flash: FlashMessage);
}

/// <summary>
/// Exercício pronto para a tela: visão segura + ids/limites vindos do progresso (gateway).
/// Nunca: solução, test_cases, common_errors.
/// </summary>
public sealed record LessonExerciseViewModel(
    string Title,
    string StatementHtml,
    string LanguageLabel,
    string? DatasetSlug,
    string? DatasetDisplayName, // ExerciseDto.DatasetDisplayName (o Backend já humaniza o slug)
    int Points,
    string? StarterCodeHtml,
    string? StarterCodePlain,
    IReadOnlyList<string> Hints,
    Guid? ExerciseId = null,
    int? MaxAttempts = null,
    int? RemainingAttempts = null,
    bool CanSkip = false,
    int FailedAttempts = 0,
    bool Passed = false,
    int? FailuresUntilSkip = null)
{
    public static LessonExerciseViewModel From(ExerciseDto exercise, LessonMarkdownRenderer renderer) => new(
        exercise.Title,
        renderer.RenderFragment(exercise.StatementMarkdown, LessonMarkdownRenderer.StatementMinHeadingLevel),
        CodeHighlighter.LabelFor(exercise.Language) ?? "Código",
        string.IsNullOrWhiteSpace(exercise.Dataset) ? null : exercise.Dataset,
        string.IsNullOrWhiteSpace(exercise.DatasetDisplayName) ? null : exercise.DatasetDisplayName,
        exercise.Points,
        string.IsNullOrWhiteSpace(exercise.StarterCode) ? null : LessonMarkdownRenderer.RenderCodeBlock(exercise.StarterCode, exercise.Language),
        string.IsNullOrWhiteSpace(exercise.StarterCode) ? null : exercise.StarterCode,
        exercise.Hints.Where(h => !string.IsNullOrWhiteSpace(h)).ToList(),
        exercise.Id == Guid.Empty ? null : exercise.Id);

    public LessonExerciseViewModel WithProgress(LessonExerciseProgressView progress) => this with
    {
        ExerciseId = ExerciseId ?? progress.ExerciseId,
        MaxAttempts = progress.MaxAttempts,
        RemainingAttempts = progress.RemainingAttempts,
        CanSkip = progress.CanSkip,
        FailedAttempts = progress.FailedAttempts,
        Passed = progress.Passed,
        FailuresUntilSkip = progress.FailuresUntilSkip,
        Points = progress.Points,
    };


    public string PointsLabel => Points == 1 ? "1 ponto" : $"{Points} pontos";

    public bool HasHints => Hints.Count > 0;

    public bool AttemptsExhausted =>
        !Passed && MaxAttempts is not null && RemainingAttempts is 0;

    public string InstructionText
    {
        get
        {
            if (MaxAttempts is null)
                return HasHints ? LessonCompletionTexts.InstructionUnlimited : LessonCompletionTexts.InstructionUnlimitedNoHints;
            return HasHints ? LessonCompletionTexts.InstructionLimited : LessonCompletionTexts.InstructionLimitedNoHints;
        }
    }
}

/// <summary>Bloco de conclusão / selo / "Seguir sem pontos" (partial `_LessonCompletion`).</summary>
public sealed record LessonCompletionViewModel(
    LessonProgressView State,
    string PathSlug,
    string ModuleSlug,
    string LessonSlug,
    string ModuleTitle,
    string PathTitle,
    string? FlashMessage = null,
    bool JustCompleted = false,
    bool JustSkipped = false,
    bool JustPassed = false,
    bool PassedAfterSkip = false)
{
    public static LessonCompletionViewModel From(LessonProgressView state, LessonDto lesson, string? flash = null, bool justCompleted = false, bool justSkipped = false) =>
        new(state, lesson.Breadcrumb.PathSlug, lesson.Breadcrumb.ModuleSlug, lesson.Slug,
            lesson.Breadcrumb.ModuleTitle, lesson.Breadcrumb.PathTitle, flash, justCompleted, justSkipped);

    /// <summary>
    /// GET .../conclusao depois de um <c>pass</c> no Executar: só anuncia (região polite), NUNCA move o foco
    /// (<see cref="MovesFocus"/> = false). <paramref name="previousKind"/> = como a aula estava antes do pass
    /// (o JS manda); <c>SkippedAfterFailures</c> + agora ExercisePassed = "ganhou +{N} pontos".
    /// </summary>
    public static LessonCompletionViewModel AfterPass(LessonProgressView state, LessonDto lesson, LessonCompletionKind? previousKind) =>
        From(state, lesson) with
        {
            JustPassed = true,
            PassedAfterSkip = previousKind == LessonCompletionKind.SkippedAfterFailures
                && state.CompletionKind == LessonCompletionKind.ExercisePassed,
        };

    /// <summary>
    /// Esta conclusão terminou o módulo (#42): o bloco mostra "Módulo concluído" no lugar da mensagem e da
    /// "Próxima aula →". Só na hora da conclusão (POST que concluiu agora ou 1º pass); revisitar a aula não mostra.
    /// </summary>
    public ModuleCompletionViewModel? ModuleCompleted { get; init; }

    /// <summary>
    /// Selo recém-desbloqueado neste pass (#45). O controller preenche a partir de
    /// <c>ExecutionOutcome.NewlyUnlockedAchievement</c> (buffer de uma leitura no Executar) ou,
    /// nos testes, do gancho do dublê. Null em refazer, skip, tentativas esgotadas e no pass
    /// de quem já tinha direito (backfill silencioso: o Backend grava o selo e deixa o campo null).
    /// </summary>
    public EarnedAchievement? JustUnlocked { get; init; }

    /// <summary>
    /// Aviso "Selo desbloqueado" só no primeiro acerto (JustPassed + selo novo). Não aparece em
    /// "Seguir sem pontos", tentativas esgotadas, reabrir a aula ou pass sem desbloqueio.
    /// </summary>
    public bool ShowUnlockNotice =>
        JustPassed
        && JustUnlocked is not null
        && State.CompletionKind == LessonCompletionKind.ExercisePassed;

    /// <summary>Só o clique em "Concluí esta aula" ou "Seguir sem pontos" leva o foco para a "Próxima".</summary>
    public bool MovesFocus => JustCompleted || JustSkipped;

    /// <summary>Mensagem que a região polite estável da página (<c>#lesson-live</c>) deve anunciar depois da troca.</summary>
    public bool Announces => MovesFocus || JustPassed;

    /// <summary>
    /// Leitura (<c>Exercise == null</c>) ou exercício com tentativas esgotadas (<see cref="LessonExerciseProgressView.AttemptsExhausted"/>).
    /// O pass conclui sozinho; esgotar MaxAttempts só libera <c>CanComplete</c> — o aluno ainda clica em Concluí.
    /// </summary>
    public bool ShowButton =>
        !State.IsCompleted
        && State.CanComplete
        && (State.Exercise is null || State.Exercise.AttemptsExhausted);

    public bool ShowDisabledExerciseButton =>
        !State.IsCompleted && !State.CanComplete && State.Blocker == LessonCompletionBlocker.ExerciseNotPassed;

    public bool ShowSkipButton =>
        !State.IsCompleted
        && State.Exercise is { CanSkip: true, Passed: false, AttemptsExhausted: false };

    public bool ShowBadge => State.IsCompleted;

    public string? SuccessMessage
    {
        get
        {
            if (FlashMessage is not null) return FlashMessage;
            if (!State.IsCompleted) return null;

            if (JustPassed && State.CompletionKind == LessonCompletionKind.ExercisePassed && State.Points is int passPoints && passPoints > 0)
            {
                return PassedAfterSkip
                    ? LessonCompletionTexts.PassAfterSkip(passPoints)
                    : LessonCompletionTexts.PassCompletes(passPoints);
            }

            if (!JustSkipped && State.CompletionKind == LessonCompletionKind.AttemptsExhausted)
            {
                // JustPassed: GET /conclusao depois do último Executar que esgotou e concluiu a aula.
                return JustCompleted || JustPassed
                    ? LessonCompletionTexts.AttemptsExhaustedJustCompleted
                    : LessonCompletionTexts.AttemptsExhaustedReopen;
            }

            if (JustSkipped || State.CompletionKind is LessonCompletionKind.SkippedAfterFailures)
            {
                if (JustSkipped || JustCompleted)
                    return LessonCompletionTexts.SkipJustCompleted;
                var earn = State.Exercise?.Points ?? State.Points;
                return earn is > 0
                    ? LessonCompletionTexts.ReopenSkipped(earn.Value)
                    : LessonCompletionTexts.ReopenReading;
            }

            if (State.CompletionKind == LessonCompletionKind.ExercisePassed && State.Points is int points && points > 0)
            {
                if (JustCompleted)
                    return LessonCompletionTexts.PassCompletes(points);
                return LessonCompletionTexts.ReopenPassed(points);
            }

            if (!JustCompleted && State.IsCompleted)
                return LessonCompletionTexts.ReopenReading;

            if (State.Next is null)
                return LessonCompletionTexts.SuccessPath(PathTitle);

            // Última do módulo: próxima é de outro módulo (slug diferente) ou não há próxima no módulo.
            if (State.Next.Lesson.ModuleSlug != ModuleSlug)
                return LessonCompletionTexts.SuccessModule(ModuleTitle);

            return LessonCompletionTexts.SuccessGeneric;
        }
    }

    public string CompleteUrl =>
        $"/trilhas/{Uri.EscapeDataString(PathSlug)}/{Uri.EscapeDataString(ModuleSlug)}/{Uri.EscapeDataString(LessonSlug)}/concluir";

    public string ConclusaoUrl =>
        $"/trilhas/{Uri.EscapeDataString(PathSlug)}/{Uri.EscapeDataString(ModuleSlug)}/{Uri.EscapeDataString(LessonSlug)}/conclusao";
}

public enum LessonUnavailableReason
{
    NotFound,
    Malformed,
    Draft,
    Locked,
    OtherPath,
}

/// <summary>Página amigável quando a aula não abre (R6 + Locked/OtherPath do #27).</summary>
public sealed record LessonUnavailableViewModel(
    LessonUnavailableReason Reason,
    string? TechnicalDetail,
    string? RequiredLessonTitle = null,
    string? RequiredLessonUrl = null,
    bool RequiredLessonCanOpen = false,
    string? CurrentPathTitle = null)
{
    public const string DraftMessage = "Esta aula está sendo escrita. Volte ao dashboard e siga pelas aulas já disponíveis.";

    public const string MalformedMessage = "Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard.";

    public string Title => Reason switch
    {
        LessonUnavailableReason.Draft => "Aula em breve",
        LessonUnavailableReason.NotFound => "Aula não encontrada",
        LessonUnavailableReason.Locked => LessonCompletionTexts.LockedTitle,
        LessonUnavailableReason.OtherPath => LessonCompletionTexts.OtherPathTitle,
        _ => "Aula indisponível",
    };

    public string Message => Reason switch
    {
        LessonUnavailableReason.Draft => DraftMessage,
        LessonUnavailableReason.NotFound => "Não encontramos esta aula. Confira o endereço ou volte ao dashboard para escolher uma aula.",
        LessonUnavailableReason.Locked => LessonCompletionTexts.LockedMessage(RequiredLessonTitle ?? "anterior"),
        LessonUnavailableReason.OtherPath => LessonCompletionTexts.OtherPathMessage(CurrentPathTitle ?? "outra trilha"),
        _ => MalformedMessage,
    };
}

public static class LevelLabels
{
    public static string For(DifficultyLevel level) => level switch
    {
        DifficultyLevel.Beginner => "Iniciante",
        DifficultyLevel.Intermediate => "Intermediário",
        DifficultyLevel.Advanced => "Avançado",
        _ => "Nível não informado",
    };
}
