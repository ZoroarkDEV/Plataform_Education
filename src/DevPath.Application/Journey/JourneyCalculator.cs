using DevPath.Domain.Progress;

namespace DevPath.Application.Journey;

/// <summary>Aula como vem do banco para o cálculo: só as publicadas, de módulos publicados, já na ordem.</summary>
public sealed record JourneyLessonInput(Guid Id, string Slug, string Title, bool IsDraft);

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
/// Progresso <see cref="ProgressStatus.Mastered"/> conta como concluído; <see cref="ProgressStatus.NotStarted"/> como sem progresso.
/// </remarks>
public static class JourneyCalculator
{
    public static DashboardDto Build(
        string pathSlug,
        string pathTitle,
        IReadOnlyList<JourneyModuleInput> modules,
        IReadOnlyDictionary<Guid, ProgressStatus> progress)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(progress);

        bool IsCompleted(JourneyLessonInput l) =>
            progress.TryGetValue(l.Id, out var s) && s is ProgressStatus.Completed or ProgressStatus.Mastered;

        bool IsStarted(JourneyLessonInput l) =>
            progress.TryGetValue(l.Id, out var s) && s is ProgressStatus.InProgress;

        LessonRefDto Ref(JourneyModuleInput m, JourneyLessonInput l) => new(pathSlug, m.Slug, l.Slug, l.Title);

        var result = new List<DashboardModuleDto>(modules.Count);
        LessonRefDto? next = null;
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

                if (next is null && status is JourneyLessonStatus.InProgress or JourneyLessonStatus.Available)
                {
                    next = Ref(module, lesson);
                }

                items.Add(new DashboardLessonDto(Ref(module, lesson), status, requirement));
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

        return new DashboardDto(
            pathSlug,
            pathTitle,
            totalCompleted,
            totalAvailable,
            Percentage(totalCompleted, totalAvailable),
            next,
            IsFirstAccess: !hasAnyProgress,
            result);
    }

    /// <summary>R3: inteiro truncado; 0 quando não há aulas disponíveis (nada de divisão por zero).</summary>
    public static int Percentage(int completed, int available) =>
        available <= 0 ? 0 : (int)(Math.Min(completed, available) * 100L / available);
}
