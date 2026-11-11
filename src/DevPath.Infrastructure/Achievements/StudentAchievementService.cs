using DevPath.Application.Achievements;
using DevPath.Domain.Achievements;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Achievements;

/// <inheritdoc />
public sealed class StudentAchievementService(DevPathDbContext db) : IStudentAchievementService
{
    private readonly AchievementGranter _granter = new(db);

    public async Task<IReadOnlyList<EarnedAchievementDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await _granter.BackfillAsync(userId, ct); // silencioso: nunca vira "desbloqueado"

        var rows = await (
                from ua in db.UserAchievements.AsNoTracking()
                join a in db.Achievements.AsNoTracking() on ua.AchievementId equals a.Id
                where ua.UserId == userId
                orderby ua.EarnedAt, a.Title
                select new { a.Title, a.Description, ua.EarnedAt })
            .ToListAsync(ct);

        return rows.Select(r => AchievementGranter.ToDto(r.Title, r.Description, r.EarnedAt)).ToList();
    }
}
