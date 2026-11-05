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
/// <item>Aula sem exercício: conclui com o clique (<see cref="LessonCompletionKind.Read"/>, sem pontos) via <see cref="Evaluate"/>.</item>
/// <item>Todo exercício com <c>pass</c>: <see cref="LessonCompletionKind.ExercisePassed"/>, soma dos pontos (<see cref="Evaluate"/>).</item>
/// <item>Exercício sem <c>pass</c> e com <c>MaxAttempts</c> esgotado: <see cref="LessonCompletionKind.AttemptsExhausted"/> via <see cref="Evaluate"/>.</item>
/// <item>"Seguir sem pontos" (≥ <c>FailuresBeforeSkip</c> falhas, padrão <see cref="DefaultFailuresBeforeSkip"/>):
/// só via <see cref="EvaluateSkip"/> → <see cref="LessonCompletionKind.SkippedAfterFailures"/>. Um <see cref="Evaluate"/>
/// normal nessa situação devolve <see cref="LessonCompletionBlocker.ExerciseNotPassed"/> (o "Concluí" não pula sozinho).</item>
/// <item>Senão: <see cref="LessonCompletionBlocker.ExerciseNotPassed"/>.</item>
/// </list>
/// Hoje há um exercício por aula; a regra vale para "cada exercício da aula" para não quebrar se vierem mais.
/// </remarks>
public static class LessonCompletionRule
{
    /// <summary>Q3 (Ibra, 29/10): depois de 5 tentativas erradas aparece o "Seguir sem pontos". Por exercício: <c>Exercise.FailuresBeforeSkip</c> (coluna do #27).</summary>
    public const int DefaultFailuresBeforeSkip = 5;

    public static int FailuresBeforeSkipFor(ExerciseAttemptsInput exercise) =>
        exercise.FailuresBeforeSkip is int n && n > 0 ? n : DefaultFailuresBeforeSkip;

    public static bool IsExhausted(ExerciseAttemptsInput e) =>
        !e.Passed && e.MaxAttempts is { } max && e.AttemptsUsed >= max;

    public static bool CanSkip(ExerciseAttemptsInput e) =>
        !e.Passed && !IsExhausted(e) && e.FailedAttempts >= FailuresBeforeSkipFor(e);

    /// <summary>
    /// Conclusão normal ("Concluí" / tentativas esgotadas / leitura / pass). Nunca produz
    /// <see cref="LessonCompletionKind.SkippedAfterFailures"/> — isso é só do <see cref="EvaluateSkip"/>.
    /// </summary>
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
        // CanSkip sozinho NÃO libera o Complete: o aluno precisa do SkipAsync explícito.
        if (pending.Any(e => !IsExhausted(e)))
        {
            return LessonCompletionDecision.Blocked(LessonCompletionBlocker.ExerciseNotPassed);
        }

        return new(true, LessonCompletionKind.AttemptsExhausted, points, null);
    }

    /// <summary>
    /// Só o "Seguir sem pontos": exige que todo exercício pendente esteja esgotado ou no limiar de falhas,
    /// e que pelo menos um esteja no limiar (<see cref="CanSkip"/>). Caso contrário, bloqueia.
    /// </summary>
    public static LessonCompletionDecision EvaluateSkip(IReadOnlyList<ExerciseAttemptsInput> exercises)
    {
        ArgumentNullException.ThrowIfNull(exercises);
        if (exercises.Count == 0 || exercises.All(e => e.Passed))
        {
            return LessonCompletionDecision.Blocked(LessonCompletionBlocker.ExerciseNotPassed);
        }

        var pending = exercises.Where(e => !e.Passed).ToList();
        if (pending.Any(e => !IsExhausted(e) && !CanSkip(e)) || !pending.Any(CanSkip))
        {
            return LessonCompletionDecision.Blocked(LessonCompletionBlocker.ExerciseNotPassed);
        }

        var points = exercises.Where(e => e.Passed).Sum(e => e.Points);
        return new(true, LessonCompletionKind.SkippedAfterFailures, points, null);
    }

    /// <summary>Visão do exercício para a tela (sem nada da solução).</summary>
    public static LessonExerciseProgressDto Describe(ExerciseAttemptsInput e) =>
        new(e.ExerciseId, e.Points, e.Passed, e.AttemptsUsed, e.FailedAttempts, e.MaxAttempts, FailuresBeforeSkipFor(e));
}
