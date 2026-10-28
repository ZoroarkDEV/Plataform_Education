using DevPath.Domain.Progress;

namespace DevPath.Application.CodeExecution;

/// <summary>
/// Corrige a tentativa do aluno: executa no sandbox contra cada caso de teste
/// e registra o resultado em <c>CodeSubmission</c>.
/// </summary>
public interface IExerciseSubmissionService
{
    /// <exception cref="KeyNotFoundException">Exercício não existe.</exception>
    /// <exception cref="NotSupportedException">Não há runner para a linguagem do exercício.</exception>
    Task<SubmissionOutcome> SubmitAsync(Guid userId, Guid exerciseId, string code, CancellationToken cancellationToken = default);
}

/// <summary>O que a tela mostra para o aluno depois de enviar.</summary>
public sealed record SubmissionOutcome(
    Guid SubmissionId,
    SubmissionResult Result,
    string Feedback,
    int ExecutionTimeMs,
    int PassedCases,
    int TotalCases);
