namespace DevPath.Application.CodeExecution;

/// <summary>
/// Corrige a tentativa do aluno: executa no sandbox contra cada caso de teste, monta a resposta da tela
/// e registra o resultado em <c>CodeSubmission</c>. Roda SÓ no Worker, que é quem fala com o Docker (ADR-006).
/// </summary>
public interface IExerciseSubmissionService
{
    /// <exception cref="ArgumentException">Código vazio, grande demais ou com caractere nulo.</exception>
    /// <exception cref="KeyNotFoundException">Exercício não existe.</exception>
    /// <exception cref="NotSupportedException">Não há runner para a linguagem do exercício.</exception>
    /// <exception cref="FormatException">Casos de teste do exercício fora do formato (erro de conteúdo).</exception>
    /// <exception cref="DatasetNotFoundException">Dataset do caso de teste não está no banco (erro de conteúdo).</exception>
    Task<ExecutionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default);
}
