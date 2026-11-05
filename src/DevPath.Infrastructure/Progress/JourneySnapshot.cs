using DevPath.Application.Journey;
using DevPath.Domain.Progress;

namespace DevPath.Infrastructure.Progress;

/// <summary>Uma aula visível (publicada, de módulo publicado) da trilha, na ordem da trilha.</summary>
public sealed record JourneyLessonRow(
    Guid LessonId,
    Guid ModuleId,
    string ModuleSlug,
    string ModuleTitle,
    string LessonSlug,
    string LessonTitle,
    bool IsDraft);

/// <summary>A linha de progresso do aluno numa aula (uma só por aula, pelo índice único).</summary>
public sealed record LessonProgressRow(
    Guid LessonId,
    ProgressStatus Status,
    DateTime LastAccessedAt,
    DateTime? CompletedAt,
    LessonCompletionKind? CompletionKind,
    int? Score);

/// <summary>
/// Foto da trilha de um aluno: roadmap visível + progresso + o resultado do <see cref="JourneyCalculator"/>.
/// Dashboard, página da aula, conclusão e execução usam esta mesma foto: o desbloqueio concorda por construção.
/// </summary>
public sealed class JourneySnapshot
{
    private readonly List<(JourneyLessonRow Row, DashboardLessonDto Dto)> _ordered;
    private readonly Dictionary<Guid, int> _index;

    public JourneySnapshot(
        Guid pathId,
        IReadOnlyList<JourneyLessonRow> lessons,
        IReadOnlyDictionary<Guid, LessonProgressRow> progress,
        DashboardDto dashboard)
    {
        PathId = pathId;
        Lessons = lessons;
        Progress = progress;
        Dashboard = dashboard;

        // O calculador mantém a ordem das linhas (módulos sem aula visível nem chegam aqui).
        var dtos = dashboard.Modules.SelectMany(m => m.Lessons).ToList();
        if (dtos.Count != lessons.Count)
        {
            throw new InvalidOperationException("Roadmap e linhas da trilha fora de sincronia.");
        }

        _ordered = lessons.Zip(dtos).ToList();
        _index = _ordered.Select((x, i) => (x.Row.LessonId, i)).ToDictionary(x => x.LessonId, x => x.i);
    }

    public Guid PathId { get; }

    public IReadOnlyList<JourneyLessonRow> Lessons { get; }

    public IReadOnlyDictionary<Guid, LessonProgressRow> Progress { get; }

    public DashboardDto Dashboard { get; }

    public bool Contains(Guid lessonId) => _index.ContainsKey(lessonId);

    public DashboardLessonDto Lesson(Guid lessonId) => _ordered[_index[lessonId]].Dto;

    /// <summary>Vizinha anterior na ordem da TRILHA (cruza módulos, rascunho incluído).</summary>
    public DashboardLessonDto? Previous(Guid lessonId) => _index[lessonId] is var i and > 0 ? _ordered[i - 1].Dto : null;

    /// <summary>Vizinha seguinte na ordem da TRILHA; null no fim da trilha.</summary>
    public DashboardLessonDto? Next(Guid lessonId) =>
        _index[lessonId] is var i && i + 1 < _ordered.Count ? _ordered[i + 1].Dto : null;

    /// <summary>
    /// <c>UserPathEnrollment.CurrentModuleId</c> (SPEC §4, ADR-007): o módulo da 1ª aula disponível (não rascunho)
    /// ainda não concluída; com tudo concluído, o último módulo que tem aula disponível; trilha sem aula = null.
    /// </summary>
    public Guid? CurrentModuleId
    {
        get
        {
            var available = _ordered.Where(x => !x.Row.IsDraft).ToList();
            var pending = available.FirstOrDefault(x => x.Dto.Status != JourneyLessonStatus.Completed);
            return pending.Row?.ModuleId ?? available.LastOrDefault().Row?.ModuleId;
        }
    }
}
