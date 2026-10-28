using DevPath.Domain.Common;

namespace DevPath.Web.ViewModels.Onboarding;

/// <summary>Referência a uma aula, com o link para a tela de aula do #12 (/trilhas/{trilha}/{modulo}/{aula}).</summary>
public sealed record LessonRef(string PathSlug, string ModuleSlug, string LessonSlug, string Title)
{
    public string Url =>
        $"/trilhas/{Uri.EscapeDataString(PathSlug)}/{Uri.EscapeDataString(ModuleSlug)}/{Uri.EscapeDataString(LessonSlug)}";
}

/// <summary>Um card da tela "Escolha sua trilha" (US-11.2).</summary>
/// <param name="AvailableLessons">Aulas que abrem (sem rascunho). Decide se a trilha pode ser escolhida.</param>
/// <param name="TotalLessons">Todas as aulas, com os rascunhos (<c>PathSummaryDto.TotalLessons</c>).</param>
public sealed record PathOptionViewModel(
    string Slug,
    string Title,
    string Description,
    DifficultyLevel Level,
    int EstimatedHours,
    int AvailableLessons,
    int TotalLessons)
{
    public string LevelLabel => Labels.PathLevel(Level);

    public string HoursLabel => EstimatedHours > 0 ? $"Cerca de {EstimatedHours} h" : "Duração a definir";

    /// <summary>
    /// Só trilha com pelo menos uma aula disponível pode ser escolhida (regra do produto, #11).
    /// Trilha sem aulas, ou só com rascunhos, aparece desabilitada com o selo "Em breve".
    /// </summary>
    public bool IsSelectable => AvailableLessons >= 1;

    /// <summary>Trilha "Em breve" que já tem aulas sendo escritas (só rascunhos), e não uma trilha vazia.</summary>
    public bool HasOnlyDrafts => AvailableLessons == 0 && TotalLessons > 0;

    public string LessonsLabel => AvailableLessons switch
    {
        0 => "Aulas em breve",
        1 => "1 aula disponível",
        _ => $"{AvailableLessons} aulas disponíveis"
    };
}

public sealed record ChoosePathViewModel(IReadOnlyList<PathOptionViewModel> Paths, string? ErrorMessage = null)
{
    public bool IsEmpty => Paths.Count == 0;

    /// <summary>Texto do card desabilitado (trilha sem aulas disponíveis).</summary>
    public const string ComingSoonExplanation =
        "Esta trilha ainda não tem aulas disponíveis. Ela poderá ser escolhida assim que a primeira aula for publicada.";
}

/// <summary>Status de uma aula no roadmap (R7: sempre com texto, não só cor).</summary>
public enum LessonStatus
{
    Completed,
    InProgress,
    Available,
    Locked,
    /// <summary>Rascunho (R4): não abre, não conta no % e não bloqueia.</summary>
    ComingSoon
}

public sealed record LessonItemViewModel(LessonRef Lesson, LessonStatus Status, LessonRef? UnlockRequirement = null)
{
    public string StatusLabel => Labels.Status(Status);

    /// <summary>Só aulas liberadas viram link; bloqueada e "Em breve" não abrem.</summary>
    public bool CanOpen => Status is LessonStatus.Completed or LessonStatus.InProgress or LessonStatus.Available;
}

public sealed record ModuleProgressViewModel(
    string Slug,
    string Title,
    int CompletedLessons,
    int AvailableLessons,
    int ProgressPercentage,
    IReadOnlyList<LessonItemViewModel> Lessons)
{
    /// <summary>Módulo sem nenhuma aula disponível (todas em rascunho).</summary>
    public bool IsComingSoon => AvailableLessons == 0;

    public string ProgressLabel => IsComingSoon
        ? "Em breve"
        : Labels.Progress(ProgressPercentage, CompletedLessons, AvailableLessons);
}

public enum DashboardState
{
    /// <summary>Trilha sem nenhuma aula disponível: "Esta trilha ainda está sendo preparada".</summary>
    Empty,
    /// <summary>Há uma próxima aula liberada e não concluída.</summary>
    Continue,
    /// <summary>100% das aulas disponíveis concluídas.</summary>
    AllDone
}

/// <summary>Dashboard da trilha atual (US-11.3). Números e status vêm prontos do serviço (R1, R3, R4).</summary>
public sealed record DashboardViewModel(
    string PathSlug,
    string PathTitle,
    int CompletedLessons,
    int AvailableLessons,
    int ProgressPercentage,
    LessonRef? NextLesson,
    bool IsFirstAccess,
    IReadOnlyList<ModuleProgressViewModel> Modules)
{
    public DashboardState State => AvailableLessons == 0
        ? DashboardState.Empty
        : NextLesson is not null ? DashboardState.Continue : DashboardState.AllDone;

    public string ProgressLabel => Labels.Progress(ProgressPercentage, CompletedLessons, AvailableLessons);

    /// <summary>No 1º acesso o botão diz "Começar pela aula 1"; depois, "Continuar".</summary>
    public string ContinueLabel => IsFirstAccess ? "Começar pela aula 1" : "Continuar";

    /// <summary>
    /// A aula pode ser aberta agora? Usado no "Conclua 'X' para liberar": se a aula que falta também está
    /// bloqueada (ou não está no roadmap), ela aparece como texto, sem link para uma página que não abre.
    /// </summary>
    public bool CanOpen(LessonRef lesson) =>
        Modules.SelectMany(m => m.Lessons).Any(item => item.Lesson == lesson && item.CanOpen);
}

/// <summary>Textos em pt-BR (R6) usados pelas telas de onboarding e dashboard.</summary>
public static class Labels
{
    public static string PathLevel(DifficultyLevel level) => level switch
    {
        DifficultyLevel.Beginner => "Para iniciantes",
        DifficultyLevel.Intermediate => "Nível intermediário",
        DifficultyLevel.Advanced => "Nível avançado",
        _ => "Nível a definir"
    };

    public static string Status(LessonStatus status) => status switch
    {
        LessonStatus.Completed => "Concluída",
        LessonStatus.InProgress => "Em andamento",
        LessonStatus.Available => "Liberada",
        LessonStatus.Locked => "Bloqueada",
        LessonStatus.ComingSoon => "Em breve",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    /// <summary>Ex.: "15% concluído — 3 de 20 aulas".</summary>
    public static string Progress(int percentage, int completed, int available) =>
        $"{Math.Clamp(percentage, 0, 100)}% concluído — {completed} de {available} {(available == 1 ? "aula" : "aulas")}";
}
