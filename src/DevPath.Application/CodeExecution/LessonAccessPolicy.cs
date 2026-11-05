namespace DevPath.Application.CodeExecution;

/// <summary>O que é preciso saber de um exercício para decidir se ele está visível.</summary>
public sealed record ExerciseVisibility(
    Guid ExerciseId,
    Guid LessonId,
    bool PathActive,
    bool ModulePublished,
    bool LessonPublished,
    bool LessonDraft,
    bool ExercisePublished)
{
    /// <summary>
    /// Trilha ativa, módulo/aula/exercício publicados e aula pronta (não rascunho).
    /// Exercício órfão (<see cref="ExercisePublished"/> = false) conta como escondido (#24).
    /// </summary>
    public static bool IsVisible(ExerciseVisibility? exercise) =>
        exercise is
        {
            PathActive: true, ModulePublished: true, LessonPublished: true,
            LessonDraft: false, ExercisePublished: true
        };
}

/// <summary>
/// Se o aluno pode executar o exercício. A Web responde 404 com a MESMA mensagem para tudo que não é
/// <see cref="Allowed"/> (não revela se o exercício existe, é de outra trilha ou está bloqueado) e registra o
/// motivo real no log (ADR-007).
/// </summary>
public enum ExerciseAccess
{
    Allowed,

    /// <summary>
    /// Não existe, está escondido (trilha inativa, módulo/aula/exercício não publicados) ou a aula é rascunho.
    /// </summary>
    NotFound,

    /// <summary>Visível, mas a aula não é da trilha atual do aluno (ou ele não tem trilha, ou a conta sumiu).</summary>
    NotEnrolled,

    /// <summary>Da trilha atual, mas a aula está bloqueada (R1): mesma regra do dashboard e da página da aula.</summary>
    Locked
}

/// <summary>Lê a visibilidade de um exercício (implementação com EF Core na Infrastructure).</summary>
public interface IExerciseCatalog
{
    Task<ExerciseVisibility?> GetVisibilityAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decide se um aluno pode executar o exercício de uma aula: visível, aula da trilha atual dele e liberada
/// (#27, ADR-007). Implementação na Infrastructure (lê o progresso com a mesma regra do dashboard).
/// </summary>
public interface ILessonAccessPolicy
{
    Task<ExerciseAccess> EvaluateAsync(Guid userId, Guid exerciseId, CancellationToken cancellationToken = default);
}
