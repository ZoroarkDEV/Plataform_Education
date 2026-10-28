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

    public static Achievement Create(string title, string description, string criteriaJson, string? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(criteriaJson);

        return new Achievement
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description.Trim(),
            Criteria = criteriaJson,
            Icon = icon
        };
    }
}
