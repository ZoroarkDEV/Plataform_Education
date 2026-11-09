using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Progress;

/// <summary>
/// Porta da Web para o progresso numa aula (issue #27): acesso à página, estado de conclusão e o "Concluí esta aula".
/// </summary>
/// <remarks>
/// <para>
/// A regra (matrícula, desbloqueio R1/R4/R5, exercício com <c>pass</c> ou tentativas esgotadas, pontos) é do Backend:
/// <c>DevPath.Application.Progress.ILessonProgressService</c> (ADR-007, seção 5). Os records abaixo espelham 1:1 os
/// do ADR-007, no formato da Web (mesma ideia do <c>Onboarding/IStudentJourneyGateway</c> do #11): o adapter para o
/// serviço real só mapeia campo a campo, com <c>switch</c> explícito nos enums. Casos esperados voltam como
/// resultado, sem exceção.
/// </para>
/// <para>
/// O registrado em <c>Program.cs</c> é o <see cref="LessonProgressGateway"/> (adapter do serviço real).
/// O dublê com estados de exemplo fica nos testes.
/// </para>
/// </remarks>
public interface ILessonProgressGateway
{
    /// <summary>Só leitura (refresh do bloco de conclusão pelo HTMX). ADR-007: <c>GetStateAsync</c>.</summary>
    Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken);

    /// <summary>Como <see cref="GetStateAsync"/> e, se <see cref="LessonAccessStatus.Allowed"/>, registra a abertura. Chamado pelo GET da aula.</summary>
    Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken);

    /// <summary>
    /// Conclui a aula pela regra normal. Idempotente: a 2ª chamada devolve
    /// <see cref="LessonCompletionStatus.AlreadyCompleted"/>. Nunca produz
    /// <see cref="LessonCompletionKind.SkippedAfterFailures"/> — use <see cref="SkipAsync"/> para
    /// "Seguir sem pontos". Sem pass, devolve <see cref="LessonCompletionStatus.RequirementNotMet"/>
    /// mesmo com <see cref="LessonExerciseProgressView.CanSkip"/>.
    /// </summary>
    Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken);

    /// <summary>
    /// "Seguir sem pontos": só conclui com <see cref="LessonCompletionKind.SkippedAfterFailures"/>
    /// quando o exercício já liberou o pulo (<see cref="LessonExerciseProgressView.CanSkip"/>).
    /// </summary>
    Task<LessonCompletionOutcome> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken);
}

/// <summary>ADR-007 <c>LessonAccessKind</c>, mais <see cref="Unavailable"/> (só da Web).</summary>
public enum LessonAccessStatus
{
    /// <summary>Matriculado e aula liberada (Completed | InProgress | Available).</summary>
    Allowed,
    /// <summary>Matriculado, aula bloqueada: <see cref="LessonAccessOutcome.State"/> traz o <c>UnlockRequirement</c>. Página 403.</summary>
    Locked,
    /// <summary>A aula não é da trilha atual (ou o aluno não tem trilha). Sem trilha: escolha; com outra trilha: 403.</summary>
    NotEnrolled,
    /// <summary>Inexistente, escondida ou rascunho: 404.</summary>
    LessonNotFound,
    /// <summary>Conta apagada com cookie válido: logout e login (como no #11).</summary>
    UserNotFound,
    /// <summary>Só da Web (dublês de teste): sem progresso. A página abre sem bloco de conclusão. O adapter real nunca devolve.</summary>
    Unavailable
}

/// <summary>ADR-007 <c>LessonAccessResult</c>.</summary>
/// <param name="State">Preenchido em Allowed e Locked.</param>
/// <param name="CurrentPathTitle">Só em NotEnrolled: título da trilha atual do aluno; null se ele não tem trilha.</param>
public sealed record LessonAccessOutcome(LessonAccessStatus Status, LessonProgressView? State = null, string? CurrentPathTitle = null)
{
    public static LessonAccessOutcome Unavailable { get; } = new(LessonAccessStatus.Unavailable);
}

/// <summary>ADR-007 <c>LessonCompletionKind</c>: como a aula foi concluída.</summary>
public enum LessonCompletionKind
{
    /// <summary>Aula só de leitura, concluída no clique. Sem pontos.</summary>
    Read,
    /// <summary>Exercício com <c>pass</c>: <see cref="LessonProgressView.Points"/> = pontos ganhos.</summary>
    ExercisePassed,
    /// <summary>
    /// "Seguir sem pontos" depois de 5 falhas (decisão do Ibra no #27; nome final no ADR-007). Concluída com 0 ponto;
    /// o exercício continua aberto e um <c>pass</c> depois promove para <see cref="ExercisePassed"/>.
    /// </summary>
    SkippedAfterFailures,
    /// <summary>
    /// Tentativas esgotadas (<c>MaxAttempts</c>, ADR-007 seção 2). Substituído na UX pelo "Seguir sem pontos":
    /// a tela trata igual a <see cref="SkippedAfterFailures"/>.
    /// </summary>
    AttemptsExhausted
}

/// <summary>ADR-007 <c>LessonCompletionBlocker</c>: por que <c>CanComplete</c> é false numa aula liberada e não concluída.</summary>
public enum LessonCompletionBlocker
{
    ExerciseNotPassed
}

/// <summary>ADR-007 <c>LessonExerciseProgressDto</c>. Só progresso: nunca solução, casos de teste ou erros comuns.</summary>
/// <param name="ExerciseId">Id para <c>POST /api/exercicios/{id}/executar</c> (até o <c>ExerciseDto.Id</c> chegar na dev, vem só por aqui).</param>
/// <param name="Points">Pontos que o exercício vale (para "+{N} pontos"; a tela nunca supõe 10).</param>
/// <param name="FailedAttempts">Envios sem <c>pass</c>.</param>
/// <param name="CanSkip">"Seguir sem pontos" disponível (a partir de <c>FailuresBeforeSkip</c> falhas, padrão 5; regra do Backend).</param>
/// <param name="FailuresUntilSkip">Falhas que faltam para o "Seguir sem pontos". A tela NÃO mostra contagem (UX); vai só em <c>data-failures-until-skip</c> para o site.js anunciar o pulo.</param>
public sealed record LessonExerciseProgressView(
    Guid ExerciseId,
    int Points,
    bool Passed,
    int AttemptsUsed,
    int? MaxAttempts,
    int? RemainingAttempts,
    int FailedAttempts = 0,
    bool CanSkip = false,
    int? FailuresUntilSkip = null)
{
    /// <summary>Alinhado ao DTO do Backend: sem pass e <c>MaxAttempts</c> esgotado.</summary>
    public bool AttemptsExhausted =>
        !Passed && MaxAttempts is not null && RemainingAttempts is 0;
}

/// <summary>ADR-007 <c>LessonProgressState</c>, no formato da Web.</summary>
/// <param name="Lesson">A própria aula (<c>LessonRefDto</c> → <see cref="LessonRef"/>).</param>
/// <param name="Status">Completed | InProgress | Available | Locked (nunca ComingSoon).</param>
/// <param name="UnlockRequirement">Só em Locked: a aula que falta concluir.</param>
/// <param name="CompletionKind">Null se não concluída.</param>
/// <param name="Points">Pontos ganhos; null se não concluída ou aula só de leitura. A tela nunca supõe 10.</param>
/// <param name="CanComplete">O "Concluí esta aula" pode ser enviado agora.</param>
/// <param name="Blocker">Por que <paramref name="CanComplete"/> é false (a Web escolhe o texto pelo valor).</param>
/// <param name="Exercise">Null = aula só de leitura.</param>
/// <param name="Previous">Vizinha anterior na ordem da TRILHA (cruza módulos; <c>DashboardLessonDto</c> → <see cref="LessonItemViewModel"/>).</param>
/// <param name="Next">Próxima na ordem da trilha; null no fim da trilha. Locked traz <c>UnlockRequirement</c>.</param>
/// <param name="PathProgressPercentage">% da trilha depois desta ação (o mesmo número do dashboard).</param>
public sealed record LessonProgressView(
    LessonRef Lesson,
    LessonStatus Status,
    LessonRef? UnlockRequirement,
    DateTimeOffset? CompletedAt,
    LessonCompletionKind? CompletionKind,
    int? Points,
    bool CanComplete,
    LessonCompletionBlocker? Blocker,
    LessonExerciseProgressView? Exercise,
    LessonItemViewModel? Previous,
    LessonItemViewModel? Next,
    int PathProgressPercentage)
{
    public bool IsCompleted => Status == LessonStatus.Completed;

    public bool HasExercise => Exercise is not null;
}

/// <summary>ADR-007 <c>LessonCompletionResultKind</c>, mais <see cref="Unavailable"/> (só da Web).</summary>
public enum LessonCompletionStatus
{
    /// <summary>Concluída agora.</summary>
    Completed,
    /// <summary>Já estava concluída (2º POST de um clique duplo). Nada muda.</summary>
    AlreadyCompleted,
    /// <summary>Liberada, mas a regra não deixa (<see cref="LessonProgressView.Blocker"/>).</summary>
    RequirementNotMet,
    /// <summary>Aula bloqueada (<see cref="LessonProgressView.UnlockRequirement"/>).</summary>
    Locked,
    NotEnrolled,
    /// <summary>Inexistente, escondida ou rascunho.</summary>
    LessonNotFound,
    UserNotFound,
    /// <summary>Só da Web (dublês de teste): sem progresso. O adapter real nunca devolve.</summary>
    Unavailable
}

/// <summary>ADR-007 <c>LessonCompletionResult</c>.</summary>
/// <param name="State">Estado DEPOIS da ação; preenchido em Completed, AlreadyCompleted, RequirementNotMet e Locked.</param>
public sealed record LessonCompletionOutcome(LessonCompletionStatus Status, LessonProgressView? State = null)
{
    public int ProgressPercentage => State?.PathProgressPercentage ?? 0;
}
