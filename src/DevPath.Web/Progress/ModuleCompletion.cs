using DevPath.Application.Progress;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Progress;

/// <summary>
/// Textos de conteúdo da tela "Módulo concluído" (#42): o subtítulo do módulo (<c>completion_subtitle</c> no
/// path.yaml) e, por aula, a frase de "O que você aprendeu" (<c>completion_summary</c> no frontmatter).
/// </summary>
/// <param name="LearnedByLessonSlug">Frase por slug de aula. Aula sem frase não aparece na lista.</param>
public sealed record ModuleCompletionContent(string? Subtitle, IReadOnlyDictionary<string, string> LearnedByLessonSlug)
{
    public static readonly ModuleCompletionContent Empty = new(null, new Dictionary<string, string>());
}

/// <summary>De onde vêm os textos de <see cref="ModuleCompletionContent"/> (porta da Web).</summary>
public interface IModuleCompletionContentSource
{
    Task<ModuleCompletionContent> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken);
}

/// <summary>
/// Adapter para o serviço do Backend (<see cref="IModuleCompletionContentService"/>, #42): só mapeia o DTO.
/// null (trilha/módulo inexistente) vira <see cref="ModuleCompletionContent.Empty"/>; a tela esconde
/// subtítulo e "O que você aprendeu". A ordem das frases é a das aulas no dashboard.
/// </summary>
public sealed class ModuleCompletionContentSource(IModuleCompletionContentService service) : IModuleCompletionContentSource
{
    public async Task<ModuleCompletionContent> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken)
    {
        var dto = await service.GetAsync(pathSlug, moduleSlug, cancellationToken);
        return dto is null ? ModuleCompletionContent.Empty : ToContent(dto);
    }

    public static ModuleCompletionContent ToContent(ModuleCompletionContentDto dto) =>
        new(dto.Subtitle, new Dictionary<string, string>(dto.LearnedByLessonSlug, StringComparer.Ordinal));
}

/// <summary>O botão principal da tela: 1ª aula que abre no próximo módulo com aula pronta.</summary>
/// <param name="Number">"Módulo N" pela posição no dashboard (a mesma numeração da lista).</param>
/// <param name="Started">O aluno já tem progresso nesse módulo (texto "Ir para" em vez de "Começar").</param>
public sealed record NextModuleStep(int Number, string Title, LessonRef FirstLesson, bool Started)
{
    public string ButtonLabel => Started
        ? ModuleCompletionTexts.GoToNextModule(Number, Title)
        : ModuleCompletionTexts.StartNextModule(Number, Title);
}

/// <summary>Tela/bloco "Módulo concluído" (#42, US-M.1). Tudo calculado do dashboard do servidor.</summary>
/// <param name="Learned">Uma frase por aula pronta do módulo, na ordem; vazia = seção escondida.</param>
/// <param name="ModulePoints">Soma dos pontos das aulas do módulo (Score do servidor).</param>
/// <param name="Next">null = não há módulo seguinte com aula que abra: a tela mostra <see cref="NoNextText"/>, sem botão.</param>
/// <param name="HasLaterModule">Há algum módulo depois deste na trilha (mesmo só com aulas "Em breve").</param>
public sealed record ModuleCompletionViewModel(
    string PathSlug,
    string PathTitle,
    string ModuleSlug,
    string ModuleTitle,
    string? Subtitle,
    IReadOnlyList<string> Learned,
    int ModulePoints,
    int PathProgressPercentage,
    NextModuleStep? Next,
    bool HasLaterModule = false)
{
    /// <summary>
    /// Sem botão: módulo seguinte só com "Em breve" = <see cref="ModuleCompletionTexts.NextModuleComingSoon"/>;
    /// nenhum módulo depois = <see cref="ModuleCompletionTexts.EverythingDone"/>. null quando há botão.
    /// </summary>
    public string? NoNextText => Next is not null
        ? null
        : HasLaterModule ? ModuleCompletionTexts.NextModuleComingSoon : ModuleCompletionTexts.EverythingDone;

    public string Title => ModuleCompletionTexts.Title(ModuleTitle);

    public string Footer => ModuleCompletionTexts.Footer(ModulePoints, PathTitle, PathProgressPercentage);

    public string SummaryUrl => SummaryUrlFor(PathSlug, ModuleSlug);

    public static string SummaryUrlFor(string pathSlug, string moduleSlug) =>
        $"/trilhas/{Uri.EscapeDataString(pathSlug)}/modulos/{Uri.EscapeDataString(moduleSlug)}/concluido";

    /// <summary>
    /// null quando o módulo não está no dashboard ou ainda não foi concluído (todas as aulas prontas concluídas;
    /// rascunho não conta, R4; módulo só com rascunhos nunca está "concluído").
    /// </summary>
    public static ModuleCompletionViewModel? TryBuild(DashboardViewModel dashboard, string moduleSlug, ModuleCompletionContent content)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(content);

        var index = dashboard.Modules.ToList().FindIndex(m => string.Equals(m.Slug, moduleSlug, StringComparison.Ordinal));
        if (index < 0 || !dashboard.Modules[index].IsCompleted)
        {
            return null;
        }

        var module = dashboard.Modules[index];
        var ready = module.Lessons.Where(l => l.Status != LessonStatus.ComingSoon).ToList();
        var learned = ready
            .Select(l => content.LearnedByLessonSlug.TryGetValue(l.Lesson.LessonSlug, out var s) ? s?.Trim() : null)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
        var points = ready.Where(l => l.Status == LessonStatus.Completed).Sum(l => Math.Max(0, l.Points ?? 0));

        return new ModuleCompletionViewModel(
            dashboard.PathSlug, dashboard.PathTitle, module.Slug, module.Title,
            string.IsNullOrWhiteSpace(content.Subtitle) ? null : content.Subtitle.Trim(),
            learned, points, dashboard.ProgressPercentage, NextStep(dashboard, index),
            HasLaterModule: index + 1 < dashboard.Modules.Count);
    }

    // Próximo módulo com aula pronta (módulo só com rascunhos é pulado, R4) e a 1ª aula dele que abre.
    // Sem nenhuma que abra: null (nada de botão para aula bloqueada ou rascunho).
    private static NextModuleStep? NextStep(DashboardViewModel dashboard, int index)
    {
        for (var i = index + 1; i < dashboard.Modules.Count; i++)
        {
            var candidate = dashboard.Modules[i];
            if (candidate.IsComingSoon)
            {
                continue;
            }

            var first = candidate.Lessons.FirstOrDefault(l => l.CanOpen && l.Status != LessonStatus.Completed)
                ?? candidate.Lessons.FirstOrDefault(l => l.CanOpen);
            if (first is null)
            {
                return null;
            }

            var started = candidate.Lessons.Any(l => l.Status is LessonStatus.Completed or LessonStatus.InProgress);
            return new NextModuleStep(i + 1, candidate.Title, first.Lesson, started);
        }

        return null;
    }
}

public enum ModuleCompletionLookupStatus
{
    /// <summary>O aluno ainda não escolheu trilha.</summary>
    NoPath,
    /// <summary>Módulo de outra trilha, inexistente ou só com rascunhos.</summary>
    NotFound,
    /// <summary>Módulo da trilha atual, ainda com aula pronta não concluída.</summary>
    NotCompleted,
    Completed
}

/// <param name="Module">Preenchido só em <see cref="ModuleCompletionLookupStatus.Completed"/>.</param>
public sealed record ModuleCompletionLookup(ModuleCompletionLookupStatus Status, ModuleCompletionViewModel? Module = null);

/// <summary>Monta o <see cref="ModuleCompletionViewModel"/> do aluno (dashboard do servidor + textos do conteúdo).</summary>
public interface IModuleCompletionReader
{
    Task<ModuleCompletionLookup> LookupAsync(Guid userId, string pathSlug, string moduleSlug, CancellationToken cancellationToken);
}

public sealed class ModuleCompletionReader(IStudentJourneyGateway journey, IModuleCompletionContentSource content)
    : IModuleCompletionReader
{
    public async Task<ModuleCompletionLookup> LookupAsync(
        Guid userId, string pathSlug, string moduleSlug, CancellationToken cancellationToken)
    {
        var dashboard = await journey.GetDashboardAsync(userId, cancellationToken);
        if (dashboard is null)
        {
            return new(ModuleCompletionLookupStatus.NoPath);
        }

        var module = string.Equals(dashboard.PathSlug, pathSlug, StringComparison.Ordinal)
            ? dashboard.Modules.FirstOrDefault(m => m.Slug == moduleSlug && !m.IsComingSoon)
            : null;
        if (module is null)
        {
            return new(ModuleCompletionLookupStatus.NotFound);
        }

        if (!module.IsCompleted)
        {
            return new(ModuleCompletionLookupStatus.NotCompleted);
        }

        var texts = await content.GetAsync(pathSlug, moduleSlug, cancellationToken);
        var built = ModuleCompletionViewModel.TryBuild(dashboard, moduleSlug, texts);
        return built is null ? new(ModuleCompletionLookupStatus.NotCompleted) : new(ModuleCompletionLookupStatus.Completed, built);
    }
}
