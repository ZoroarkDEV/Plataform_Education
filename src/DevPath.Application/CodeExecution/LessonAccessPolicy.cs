namespace DevPath.Application.CodeExecution;

/// <summary>O que é preciso saber de um exercício para decidir se o aluno pode executá-lo.</summary>
public sealed record ExerciseVisibility(
    Guid ExerciseId,
    Guid LessonId,
    bool PathActive,
    bool ModulePublished,
    bool LessonPublished,
    bool LessonDraft);

public enum ExerciseAccess
{
    Allowed,

    /// <summary>Não existe ou está escondido (trilha inativa, módulo/aula não publicados): a Web responde 404.</summary>
    NotFound,

    /// <summary>Aula em construção: aparece na navegação, mas ainda não abre. A Web responde 403.</summary>
    Draft
}

/// <summary>Lê a visibilidade de um exercício (implementação com EF Core na Infrastructure).</summary>
public interface IExerciseCatalog
{
    Task<ExerciseVisibility?> GetVisibilityAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}

/// <summary>Decide se um aluno pode executar o exercício de uma aula.</summary>
public interface ILessonAccessPolicy
{
    ExerciseAccess Evaluate(Guid userId, ExerciseVisibility? exercise);
}

/// <summary>
/// Regra atual: qualquer aluno logado executa exercícios de aulas visíveis e prontas.
/// </summary>
/// <remarks>
/// TODO(#15 / progresso): liberar por progresso (ex.: só abre a aula N depois de concluir a N-1).
/// Quando isso existir, esta classe recebe o progresso do aluno e devolve um novo valor "Locked".
/// </remarks>
public sealed class LessonAccessPolicy : ILessonAccessPolicy
{
    public ExerciseAccess Evaluate(Guid userId, ExerciseVisibility? exercise)
    {
        if (exercise is null || !exercise.PathActive || !exercise.ModulePublished || !exercise.LessonPublished)
        {
            return ExerciseAccess.NotFound;
        }

        return exercise.LessonDraft ? ExerciseAccess.Draft : ExerciseAccess.Allowed;
    }
}
