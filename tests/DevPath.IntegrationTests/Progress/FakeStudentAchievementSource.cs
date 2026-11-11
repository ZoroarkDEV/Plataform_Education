using DevPath.Web.Progress;

namespace DevPath.IntegrationTests.Progress;

/// <summary>Dublê da porta de selos (#45): devolve a lista e o desbloqueio que o teste configurar.</summary>
public sealed class FakeStudentAchievementSource : IStudentAchievementSource
{
    public IReadOnlyList<EarnedAchievement> Items { get; init; } = [];

    public EarnedAchievement? NewlyUnlocked { get; init; }

    public Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items);

    public Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(NewlyUnlocked);
}
