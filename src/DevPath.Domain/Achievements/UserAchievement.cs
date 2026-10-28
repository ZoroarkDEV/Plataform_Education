namespace DevPath.Domain.Achievements;

/// <summary>Registro de que um aluno ganhou uma conquista. Chave composta: (UserId, AchievementId).</summary>
/// <remarks>
/// UserId aponta para o ApplicationUser (Identity, na Infrastructure). A FK existe no banco,
/// mas não há navegação aqui para o Domain não depender do Identity.
/// </remarks>
public sealed class UserAchievement
{
    public Guid UserId { get; private set; }
    public Guid AchievementId { get; private set; }
    public DateTime EarnedAt { get; private set; }

    private UserAchievement() { }

    public static UserAchievement Grant(Guid userId, Guid achievementId) =>
        new() { UserId = userId, AchievementId = achievementId, EarnedAt = DateTime.UtcNow };
}
