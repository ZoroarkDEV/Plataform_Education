namespace DevPath.Application.Progress;

/// <summary>
/// Progresso do aluno numa aula (#27, ADR-007): abrir (registra "em andamento"), ler o estado e concluir.
/// Não lança para os casos esperados: o resultado diz o que aconteceu (mesmo estilo do IStudentJourneyService).
/// </summary>
/// <remarks>
/// O desbloqueio é SEMPRE a regra do dashboard (<c>JourneyCalculator</c>), sobre os mesmos dados: dashboard,
/// página da aula, paginação, conclusão e execução de exercício concordam por construção.
/// </remarks>
public interface ILessonProgressService
{
    /// <summary>Só leitura (bloco de conclusão via HTMX, paginação).</summary>
    Task<LessonAccessResult> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);

    /// <summary>
    /// O mesmo que <see cref="GetStateAsync"/> e, se a aula está liberada, registra a abertura: cria o progresso
    /// <c>InProgress</c> ou só atualiza <c>LastAccessedAt</c> (aula concluída continua concluída). Chamado pelo GET da aula.
    /// Aula bloqueada, de outra trilha ou rascunho não grava nada.
    /// </summary>
    Task<LessonAccessResult> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);

    /// <summary>
    /// Conclui a aula pela regra normal (<see cref="LessonCompletionRule.Evaluate"/>) e recalcula a matrícula na mesma
    /// transação. Idempotente e seguro com cliques simultâneos. Nunca produz <c>SkippedAfterFailures</c> — use
    /// <see cref="SkipAsync"/> para o "Seguir sem pontos".
    /// </summary>
    Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);

    /// <summary>
    /// "Seguir sem pontos": só conclui com <c>SkippedAfterFailures</c> quando a regra
    /// <see cref="LessonCompletionRule.EvaluateSkip"/> permite. Um "Concluí" normal continua em
    /// <see cref="CompleteAsync"/> e devolve <c>RequirementNotMet</c> se o exercício ainda não passou.
    /// </summary>
    Task<LessonCompletionResult> SkipAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);
}
