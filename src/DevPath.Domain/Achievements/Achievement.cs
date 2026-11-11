namespace DevPath.Domain.Achievements;

/// <summary>Uma conquista/badge (ex.: "Primeira query SQL").</summary>
public sealed class Achievement
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Icon { get; private set; }

    /// <summary>Regra para ganhar a conquista, em JSON (gravado como <c>jsonb</c>).</summary>
    public string Criteria { get; private set; } = "{}";

    private Achievement() { }

    public static Achievement Create(string title, string description, string criteriaJson, string? icon = null) =>
        CreateWithId(Guid.NewGuid(), title, description, criteriaJson, icon);

    /// <summary>
    /// Conquista com id FIXO (seed/testes), ex.: <see cref="AchievementIds.PrimeiroSelect"/>. O seed de verdade é a
    /// migration <c>AddPrimeiroSelectAchievement</c> (idempotente, <c>ON CONFLICT DO NOTHING</c>).
    /// </summary>
    public static Achievement CreateWithId(Guid id, string title, string description, string criteriaJson, string? icon = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O id da conquista não pode ser vazio.", nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(criteriaJson);

        return new Achievement
        {
            Id = id,
            Title = title.Trim(),
            Description = description.Trim(),
            Criteria = criteriaJson,
            Icon = icon
        };
    }

    /// <summary>O selo "Primeiro SELECT" (#45), com o id, o texto e o critério fixos de <see cref="AchievementIds"/>.</summary>
    public static Achievement PrimeiroSelect() => CreateWithId(AchievementIds.PrimeiroSelect, AchievementIds.PrimeiroSelectTitle,
        AchievementIds.PrimeiroSelectDescription, AchievementIds.PrimeiroSelectCriteria);
}
