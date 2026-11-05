using DevPath.Domain.Progress;

namespace DevPath.Application.Progress;

/// <summary>O que a regra de conclusão precisa saber de UM exercício da aula, para UM aluno.</summary>
/// <param name="MaxAttempts">Limite de tentativas do exercício; null = sem limite.</param>
/// <param name="FailuresBeforeSkip">Limiar do "Seguir sem pontos" do exercício; null = <see cref="LessonCompletionRule.DefaultFailuresBeforeSkip"/>.</param>
/// <param name="AttemptsUsed">Submissões gravadas (todas, como no limite de tentativas do Worker).</param>
/// <param name="FailedAttempts">Submissões gravadas com resultado <c>Fail</c> (erro e timeout do aluno também são Fail).</param>
/// <param name="Passed">Existe ao menos uma submissão <c>Pass</c>.</param>
public sealed record ExerciseAttemptsInput(
    Guid ExerciseId,
    int Points,
    int? MaxAttempts,
    int? FailuresBeforeSkip,
    int AttemptsUsed,
    int FailedAttempts,
    bool Passed);

/// <summary>Resultado da regra: se pode concluir agora, como e com quantos pontos; ou por que não.</summary>
/// <param name="Points">Pontos ganhos ao concluir; null em aula só de leitura (Q5: leitura não vale pontos).</param>
public sealed record LessonCompletionDecision(
    bool CanComplete,
    LessonCompletionKind? Kind,
    int? Points,
    LessonCompletionBlocker? Blocker)
{
    public static LessonCompletionDecision Blocked(LessonCompletionBlocker blocker) => new(false, null, null, blocker);
}

/// <summary>
/// A regra de conclusão de aula (#27, ADR-007), num lugar só e sem banco. Se o Manager mudar a regra,
/// muda só aqui (e os testes de unidade dela).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Aula sem exercício: conclui com o clique (<see cref="LessonCompletionKind.Read"/>, sem pontos).</item>
/// <item>Todo exercício com <c>pass</c>: <see cref="LessonCompletionKind.ExercisePassed"/>, soma dos pontos.</item>
/// <item>Exercício sem <c>pass</c> pode ser "dispensado" se: usou todas as tentativas (<c>MaxAttempts</c>) →
/// <see cref="LessonCompletionKind.AttemptsExhausted"/>; ou errou pelo menos <c>FailuresBeforeSkip</c> vezes (padrão
/// <see cref="DefaultFailuresBeforeSkip"/>) → "Seguir sem pontos", <see cref="LessonCompletionKind.SkippedAfterFailures"/>.
/// Pontos = só os dos exercícios que passaram.</item>
/// <item>Senão: <see cref="LessonCompletionBlocker.ExerciseNotPassed"/>.</item>
/// </list>
/// Hoje há um exercício por aula; a regra vale para "cada exercício da aula" para não quebrar se vierem mais.
/// </remarks>
public static class LessonCompletionRule
{
    /// <summary>Q3 (Ibra, 29/10): depois de 5 tentativas erradas aparece o "Seguir sem pontos". Por exercício: <c>Exercise.FailuresBeforeSkip</c> (#24).</summary>
    public const int DefaultFailuresBeforeSkip = 5;

    public static int FailuresBeforeSkipFor(ExerciseAttemptsInput exercise) =>
        exercise.FailuresBeforeSkip is int n && n > 0 ? n : DefaultFailuresBeforeSkip;

    public static bool IsExhausted(ExerciseAttemptsInput e) =>
        !e.Passed && e.MaxAttempts is { } max && e.AttemptsUsed >= max;

    public static bool CanSkip(ExerciseAttemptsInput e) =>
        !e.Passed && !IsExhausted(e) && e.FailedAttempts >= FailuresBeforeSkipFor(e);

    public static LessonCompletionDecision Evaluate(IReadOnlyList<ExerciseAttemptsInput> exercises)
    {
        ArgumentNullException.ThrowIfNull(exercises);
        if (exercises.Count == 0)
        {
            return new(true, LessonCompletionKind.Read, null, null);
        }

        var points = exercises.Where(e => e.Passed).Sum(e => e.Points);
        if (exercises.All(e => e.Passed))
        {
            return new(true, LessonCompletionKind.ExercisePassed, points, null);
        }

        var pending = exercises.Where(e => !e.Passed).ToList();
        if (pending.Any(e => !IsExhausted(e) && !CanSkip(e)))
        {
            return LessonCompletionDecision.Blocked(LessonCompletionBlocker.ExerciseNotPassed);
        }

        // Se algum ainda dá para tentar, o aluno está "seguindo sem pontos"; só esgotado = sem volta.
        var kind = pending.Any(CanSkip) ? LessonCompletionKind.SkippedAfterFailures : LessonCompletionKind.AttemptsExhausted;
        return new(true, kind, points, null);
    }

    /// <summary>Visão do exercício para a tela (sem nada da solução).</summary>
    public static LessonExerciseProgressDto Describe(ExerciseAttemptsInput e) =>
        new(e.ExerciseId, e.Points, e.Passed, e.AttemptsUsed, e.FailedAttempts, e.MaxAttempts, FailuresBeforeSkipFor(e));
}
