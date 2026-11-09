using DevPath.Application.CodeExecution;
using DevPath.Domain.Progress;

namespace DevPath.Application.Progress;

/// <summary>Contagem de tentativas de UM aluno em UM exercício.</summary>
/// <param name="Used">Submissões gravadas (o que o <c>MaxAttempts</c> limita).</param>
/// <param name="Failed">Submissões gravadas com <see cref="SubmissionResult.Fail"/> (o que libera o "Seguir sem pontos").</param>
/// <param name="Passed">Há pelo menos um <see cref="SubmissionResult.Pass"/>.</param>
public readonly record struct AttemptCounts(int Used, int Failed, bool Passed);

/// <summary>
/// O que é "uma tentativa errada" (#27, ADR-007 Decisões), num lugar só para o Worker e para o progresso concordarem.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Tentativa = <c>CodeSubmission</c> GRAVADA. O que é recusado antes de rodar não grava e não conta:
/// 400 (código vazio/grande), 429 (rate limit), 503 (executor fora), tentativas esgotadas e erro de
/// conteúdo/preparação do exercício (culpa nossa, não do aluno).</item>
/// <item>Errada = resultado <c>fail</c>, <c>error</c> (erro de sintaxe ou de execução do aluno, memória, saída
/// grande) ou <c>timeout</c>. Os três são gravados como <see cref="SubmissionResult.Fail"/>.</item>
/// <item>Um <c>pass</c> sempre vale os pontos cheios do exercício, sem desconto por tentativas.</item>
/// </list>
/// </remarks>
public static class AttemptCounting
{
    /// <summary>Como uma execução que RODOU é gravada. Só <c>pass</c> é Pass; fail, error e timeout são Fail.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Status que não grava tentativa (ex.: tentativas esgotadas).</exception>
    public static SubmissionResult ResultFor(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Pass => SubmissionResult.Pass,
        ExecutionStatus.Fail or ExecutionStatus.Error or ExecutionStatus.Timeout => SubmissionResult.Fail,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Este status não grava tentativa.")
    };

    /// <summary>O detalhe gravado junto (#43): certo, errado, erro ou timeout. Mesmos status que <see cref="ResultFor"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Status que não grava tentativa.</exception>
    public static SubmissionOutcome OutcomeFor(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Pass => SubmissionOutcome.Pass,
        ExecutionStatus.Fail => SubmissionOutcome.Fail,
        ExecutionStatus.Error => SubmissionOutcome.Error,
        ExecutionStatus.Timeout => SubmissionOutcome.Timeout,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Este status não grava tentativa.")
    };

    public static AttemptCounts Count(IEnumerable<SubmissionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int used = 0, failed = 0;
        var passed = false;
        foreach (var result in results)
        {
            used++;
            if (result == SubmissionResult.Fail)
            {
                failed++;
            }
            else
            {
                passed = true;
            }
        }

        return new(used, failed, passed);
    }
}
