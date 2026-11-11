namespace DevPath.Domain.Achievements;

/// <summary>
/// Ids FIXOS das conquistas que o sistema concede (#45). Fixos para o seed (migration), o código e os testes
/// apontarem para a mesma linha de <c>Achievements</c> em qualquer banco. Nunca mude um id já publicado.
/// </summary>
public static class AchievementIds
{
    /// <summary>Texto do id de <see cref="PrimeiroSelect"/> (Guid não pode ser <c>const</c>).</summary>
    public const string PrimeiroSelectValue = "5e1ec700-0001-4000-8000-000000000001";

    /// <summary>Selo "Primeiro SELECT" (US-BADGE.1): primeiro acerto de exercício da vida do aluno.</summary>
    public static readonly Guid PrimeiroSelect = Guid.Parse(PrimeiroSelectValue);

    /// <summary>Slug gravado no <c>Criteria</c> do selo: <c>{"slug":"primeiro-select"}</c>.</summary>
    public const string PrimeiroSelectSlug = "primeiro-select";

    public const string PrimeiroSelectTitle = "Primeiro SELECT";

    public const string PrimeiroSelectDescription = "Você fez um banco de dados responder à sua primeira pergunta.";

    /// <summary>JSON do <c>Criteria</c> do selo (coluna <c>jsonb</c>).</summary>
    public const string PrimeiroSelectCriteria = """{"slug":"primeiro-select"}""";

    /// <summary>
    /// Aula cujo <c>ExercisePassed</c> antigo (antes do #45) já dá direito ao selo no backfill silencioso.
    /// </summary>
    public const string PrimeiroSelectBackfillLessonSlug = "seu-primeiro-select";
}
