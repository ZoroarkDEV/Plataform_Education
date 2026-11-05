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
    /// Conclui a aula pela <see cref="LessonCompletionRule"/> e recalcula a matrícula na mesma transação. Idempotente
    /// e seguro com cliques simultâneos (uma linha de progresso por aluno e aula).
    /// </summary>
    Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);
}
