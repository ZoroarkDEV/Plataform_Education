using DevPath.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class UserAchievementConfiguration : IEntityTypeConfiguration<UserAchievement>
{
    public void Configure(EntityTypeBuilder<UserAchievement> builder)
    {
        builder.ToTable("UserAchievements");

        // Chave composta (UserId, AchievementId): a mesma conquista não é ganha duas vezes.
        builder.HasKey(ua => new { ua.UserId, ua.AchievementId });

        builder.HasOne<Achievement>().WithMany().HasForeignKey(ua => ua.AchievementId).OnDelete(DeleteBehavior.Cascade);
    }
}
