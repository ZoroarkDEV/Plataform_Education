using DevPath.Application.Journey;
using DevPath.Domain.Progress;

namespace DevPath.Application.Progress;

/// <summary>Se o aluno pode abrir uma aula (ADR-007 §1). Ordem: UserNotFound &gt; LessonNotFound &gt; NotEnrolled &gt; Locked &gt; Allowed.</summary>
public enum LessonAccessKind
{
    /// <summary>Matriculado na trilha da aula e a aula está liberada (Completed, InProgress ou Available).</summary>
    Allowed,

    /// <summary>Matriculado, mas a aula está bloqueada. <see cref="LessonAccessResult.State"/> vem com o UnlockRequirement.</summary>
    Locked,

    /// <summary>A aula existe, mas não é da trilha atual do aluno (ou ele não tem trilha). Veja <see cref="LessonAccessResult.CurrentPathTitle"/>.</summary>
    NotEnrolled,

    /// <summary>Não existe, está escondida (trilha inativa, módulo/aula não publicados) ou é rascunho.</summary>
    LessonNotFound,

    /// <summary>Conta apagada com o cookie ainda válido.</summary>
    UserNotFound
}

/// <param name="State">Preenchido em <see cref="LessonAccessKind.Allowed"/> e <see cref="LessonAccessKind.Locked"/>.</param>
/// <param name="CurrentPathTitle">Só em <see cref="LessonAccessKind.NotEnrolled"/>: título da trilha atual; null = o aluno não tem trilha.</param>
public sealed record LessonAccessResult(LessonAccessKind Kind, LessonProgressState? State = null, string? CurrentPathTitle = null)
{
    public static LessonAccessResult NotFound() => new(LessonAccessKind.LessonNotFound);
    public static LessonAccessResult UserNotFound() => new(LessonAccessKind.UserNotFound);
    public static LessonAccessResult NotEnrolled(string? currentPathTitle) => new(LessonAccessKind.NotEnrolled, null, currentPathTitle);
}

/// <summary>Por que uma aula liberada e não concluída ainda não pode ser concluída.</summary>
public enum LessonCompletionBlocker
{
    /// <summary>O exercício da aula ainda não passou e não esgotou nem liberou o "Seguir sem pontos".</summary>
    ExerciseNotPassed
}

/// <summary>Situação do aluno num exercício da aula. Nunca traz solução nem casos de teste.</summary>
/// <param name="AttemptsUsed">Submissões gravadas.</param>
/// <param name="FailedAttempts">Submissões erradas (Fail).</param>
/// <param name="MaxAttempts">Limite de tentativas; null = sem limite.</param>
/// <param name="FailuresBeforeSkip">Erros que liberam o "Seguir sem pontos" (padrão 5).</param>
public sealed record LessonExerciseProgressDto(
    Guid ExerciseId,
    int Points,
    bool Passed,
    int AttemptsUsed,
    int FailedAttempts,
    int? MaxAttempts,
    int FailuresBeforeSkip)
{
    /// <summary>Tentativas restantes; null = sem limite.</summary>
    public int? RemainingAttempts => MaxAttempts is { } max ? Math.Max(0, max - AttemptsUsed) : null;

    /// <summary>Usou todas as tentativas sem passar: não dá mais para executar.</summary>
    public bool AttemptsExhausted => !Passed && MaxAttempts is { } max && AttemptsUsed >= max;

    /// <summary>Pode "Seguir sem pontos" (e continua podendo tentar).</summary>
    public bool CanSkip => !Passed && !AttemptsExhausted && FailedAttempts >= FailuresBeforeSkip;

    /// <summary>Quantos erros ainda faltam para liberar o "Seguir sem pontos"; 0 quando já liberou, passou ou esgotou.</summary>
    public int FailuresUntilSkip => Passed || AttemptsExhausted ? 0 : Math.Max(0, FailuresBeforeSkip - FailedAttempts);
}

/// <summary>Estado do aluno numa aula da trilha atual (página da aula, bloco de conclusão e paginação).</summary>
/// <param name="Lesson">Endereço e título da aula (o mesmo <see cref="LessonRefDto"/> do dashboard).</param>
/// <param name="Status">Completed, InProgress, Available ou Locked (nunca ComingSoon: rascunho é LessonNotFound).</param>
/// <param name="UnlockRequirement">Só em Locked: a aula que falta concluir (o mesmo do dashboard).</param>
/// <param name="CompletionKind">Como concluiu; null se não concluída.</param>
/// <param name="Points">Pontos ganhos; null se não concluída ou aula só de leitura.</param>
/// <param name="CanComplete">Liberada, não concluída e <see cref="LessonCompletionRule.Evaluate"/> permite agora (não inclui "Seguir sem pontos"; isso é <c>Exercise.CanSkip</c> + <see cref="ILessonProgressService.SkipAsync"/>).</param>
/// <param name="Blocker">Por que CanComplete = false numa aula liberada e não concluída.</param>
/// <param name="Exercise">O exercício da aula (o mesmo que a página mostra); null = aula só de leitura.</param>
/// <param name="Previous">Aula anterior na ordem da TRILHA (cruza módulos, rascunho incluído); null na primeira.</param>
/// <param name="Next">Próxima aula na ordem da TRILHA; null no fim da trilha. Locked traz o UnlockRequirement.</param>
/// <param name="PathProgressPercentage">% da trilha (o mesmo número do dashboard).</param>
/// <param name="PathTotalPoints">Pontos totais da trilha (o mesmo do dashboard; UX I6).</param>
public sealed record LessonProgressState(
    LessonRefDto Lesson,
    JourneyLessonStatus Status,
    LessonRefDto? UnlockRequirement,
    DateTimeOffset? CompletedAt,
    LessonCompletionKind? CompletionKind,
    int? Points,
    bool CanComplete,
    LessonCompletionBlocker? Blocker,
    LessonExerciseProgressDto? Exercise,
    DashboardLessonDto? Previous,
    DashboardLessonDto? Next,
    int PathProgressPercentage,
    int PathTotalPoints = 0)
{
    public bool IsCompleted => Status == JourneyLessonStatus.Completed;
}

/// <summary>Resultado de <see cref="ILessonProgressService.CompleteAsync"/> e <see cref="ILessonProgressService.SkipAsync"/>.</summary>
/// <remarks>Ordem: UserNotFound &gt; LessonNotFound &gt; NotEnrolled &gt; Locked &gt; AlreadyCompleted &gt; RequirementNotMet &gt; Completed.</remarks>
public enum LessonCompletionResultKind
{
    /// <summary>Concluiu agora.</summary>
    Completed,

    /// <summary>Já estava concluída (idempotente: nada muda; 2º POST de um duplo clique).</summary>
    AlreadyCompleted,

    /// <summary>Liberada, mas a regra de conclusão não permite (State.Blocker diz o motivo).</summary>
    RequirementNotMet,

    /// <summary>Aula bloqueada (State.UnlockRequirement).</summary>
    Locked,

    NotEnrolled,

    /// <summary>Inexistente, escondida ou rascunho.</summary>
    LessonNotFound,

    UserNotFound
}

/// <param name="State">Estado DEPOIS da ação; preenchido em Completed, AlreadyCompleted, RequirementNotMet e Locked.</param>
public sealed record LessonCompletionResult(LessonCompletionResultKind Kind, LessonProgressState? State = null)
{
    /// <summary>% da trilha depois da ação (o mesmo do dashboard); 0 quando não há estado.</summary>
    public int ProgressPercentage => State?.PathProgressPercentage ?? 0;
}
