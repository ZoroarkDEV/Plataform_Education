using DevPath.Domain.Progress;

namespace DevPath.Application.Journey;

/// <summary>Aula como vem do banco para o cálculo: só as publicadas, de módulos publicados, já na ordem.</summary>
public sealed record JourneyLessonInput(Guid Id, string Slug, string Title, bool IsDraft);

/// <summary>Progresso do aluno numa aula, como o cálculo precisa (ADR-007).</summary>
/// <param name="LastAccessedAt">Última abertura (UTC): decide o "Continuar".</param>
/// <param name="CompletionKind">Como concluiu (só em concluída).</param>
/// <param name="Points">Pontos da conclusão (<c>UserProgress.Score</c>); null em leitura ou não concluída.</param>
public sealed record JourneyProgressInput(
    ProgressStatus Status,
    DateTime LastAccessedAt,
    LessonCompletionKind? CompletionKind = null,
    int? Points = null);

/// <summary>Módulo publicado com as aulas publicadas dele, já na ordem.</summary>
public sealed record JourneyModuleInput(string Slug, string Title, IReadOnlyList<JourneyLessonInput> Lessons);

/// <summary>
/// Função pura que monta o dashboard a partir do conteúdo visível e do progresso do aluno.
/// Sem banco: é aqui que ficam as regras R1, R3 e R4 (testadas em unidade, SPEC §9.1).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>R4:</b> rascunho = <see cref="JourneyLessonStatus.ComingSoon"/>, fora do % e nunca bloqueia
/// (é pulado na hora de ver "qual aula vem antes"). Módulo só com rascunhos fica 0/0 e não segura o próximo.</item>
/// <item><b>R1:</b> a 1ª aula disponível da trilha está sempre liberada. Dentro do módulo, uma aula exige a
/// aula disponível anterior concluída. A 1ª aula disponível de um módulo exige <b>todas</b> as aulas
/// disponíveis do módulo anterior (o anterior que tem aulas disponíveis) concluídas.</item>
/// <item><b>Requisito mostrado (US-11.4):</b> dentro do módulo, a aula anterior. Na virada de módulo, a última
/// aula ainda não concluída do módulo anterior (quase sempre a última dele).</item>
/// <item><b>R5:</b> aula concluída ou em andamento nunca aparece bloqueada, mesmo que o conteúdo mude
/// (ex.: aula nova inserida antes dela).</item>
/// <item><b>R3:</b> % = concluídas × 100 ÷ disponíveis, inteiro truncado (3 de 20 → 15). 0 disponíveis → 0.</item>
/// </list>
/// <item><b>"Continuar" (Q6, ADR-007):</b> a aula <see cref="JourneyLessonStatus.InProgress"/> aberta por último
/// (<see cref="JourneyProgressInput.LastAccessedAt"/>; empate = a primeira na ordem); sem nenhuma, a 1ª
/// <see cref="JourneyLessonStatus.Available"/>; tudo concluído = null.</item>
/// </list>
/// Progresso <see cref="ProgressStatus.Mastered"/> conta como concluído; <see cref="ProgressStatus.NotStarted"/> como sem progresso.
/// </remarks>
public static class JourneyCalculator
{
    /// <summary>Sobrecarga antiga (só status). Sem data de acesso, o "Continuar" fica com a 1ª aula em andamento.</summary>
    public static DashboardDto Build(
        string pathSlug,
        string pathTitle,
        IReadOnlyList<JourneyModuleInput> modules,
        IReadOnlyDictionary<Guid, ProgressStatus> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return Build(pathSlug, pathTitle, modules,
            progress.ToDictionary(p => p.Key, p => new JourneyProgressInput(p.Value, DateTime.MinValue)));
    }

    public static DashboardDto Build(
        string pathSlug,
        string pathTitle,
        IReadOnlyList<JourneyModuleInput> modules,
        IReadOnlyDictionary<Guid, JourneyProgressInput> progressInput)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(progressInput);
        var progress = progressInput.ToDictionary(p => p.Key, p => p.Value.Status);

        bool IsCompleted(JourneyLessonInput l) =>
            progress.TryGetValue(l.Id, out var s) && s is ProgressStatus.Completed or ProgressStatus.Mastered;

        bool IsStarted(JourneyLessonInput l) =>
            progress.TryGetValue(l.Id, out var s) && s is ProgressStatus.InProgress;

        LessonRefDto Ref(JourneyModuleInput m, JourneyLessonInput l) => new(pathSlug, m.Slug, l.Slug, l.Title);

        var result = new List<DashboardModuleDto>(modules.Count);
        LessonRefDto? firstAvailable = null;
        (LessonRefDto Lesson, DateTime At)? lastStarted = null;
        var hasAnyProgress = false;

        // Aulas disponíveis do último módulo que tinha alguma (o "módulo anterior" do R1).
        (JourneyModuleInput Module, List<JourneyLessonInput> Lessons)? previousModule = null;

        foreach (var module in modules)
        {
            if (module.Lessons.Count == 0)
            {
                continue; // Módulo publicado sem aula publicada: não há o que mostrar.
            }

            var items = new List<DashboardLessonDto>(module.Lessons.Count);
            var available = new List<JourneyLessonInput>();
            JourneyLessonInput? previousInModule = null;
            var completed = 0;

            foreach (var lesson in module.Lessons)
            {
                hasAnyProgress |= progress.TryGetValue(lesson.Id, out var any) && any != ProgressStatus.NotStarted;

                if (lesson.IsDraft)
                {
                    items.Add(new DashboardLessonDto(Ref(module, lesson), JourneyLessonStatus.ComingSoon));
                    continue;
                }

                JourneyLessonStatus status;
                LessonRefDto? requirement = null;

                if (IsCompleted(lesson))
                {
                    status = JourneyLessonStatus.Completed;
                    completed++;
                }
                else if (IsStarted(lesson))
                {
                    status = JourneyLessonStatus.InProgress;
                }
                else
                {
                    if (previousInModule is not null)
                    {
                        if (!IsCompleted(previousInModule))
                        {
                            requirement = Ref(module, previousInModule);
                        }
                    }
                    else if (previousModule is { } prev)
                    {
                        var missing = prev.Lessons.LastOrDefault(l => !IsCompleted(l));
                        if (missing is not null)
                        {
                            requirement = Ref(prev.Module, missing);
                        }
                    }

                    status = requirement is null ? JourneyLessonStatus.Available : JourneyLessonStatus.Locked;
                }

                if (status == JourneyLessonStatus.InProgress)
                {
                    var at = progressInput[lesson.Id].LastAccessedAt;
                    if (lastStarted is null || at > lastStarted.Value.At)
                    {
                        lastStarted = (Ref(module, lesson), at);
                    }
                }
                else if (firstAvailable is null && status == JourneyLessonStatus.Available)
                {
                    firstAvailable = Ref(module, lesson);
                }

                var done = status == JourneyLessonStatus.Completed ? progressInput[lesson.Id] : null;
                items.Add(new DashboardLessonDto(Ref(module, lesson), status, requirement, done?.CompletionKind, done?.Points));
                available.Add(lesson);
                previousInModule = lesson;
            }

            result.Add(new DashboardModuleDto(
                module.Slug, module.Title, completed, available.Count, Percentage(completed, available.Count), items));

            if (available.Count > 0)
            {
                previousModule = (module, available);
            }
        }

        var totalCompleted = result.Sum(m => m.CompletedLessons);
        var totalAvailable = result.Sum(m => m.AvailableLessons);
        var totalPoints = result.SelectMany(m => m.Lessons)
            .Where(l => l.Status == JourneyLessonStatus.Completed && l.Points is > 0)
            .Sum(l => l.Points!.Value);

        return new DashboardDto(
            pathSlug,
            pathTitle,
            totalCompleted,
            totalAvailable,
            Percentage(totalCompleted, totalAvailable),
            lastStarted?.Lesson ?? firstAvailable,
            IsFirstAccess: !hasAnyProgress,
            result,
            totalPoints);
    }

    /// <summary>R3: inteiro truncado; 0 quando não há aulas disponíveis (nada de divisão por zero).</summary>
    public static int Percentage(int completed, int available) =>
        available <= 0 ? 0 : (int)(Math.Min(completed, available) * 100L / available);
}
