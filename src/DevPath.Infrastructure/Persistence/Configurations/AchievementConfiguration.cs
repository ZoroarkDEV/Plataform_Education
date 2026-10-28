using DevPath.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.ToTable("Achievements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(2000);
        builder.Property(a => a.Icon).HasMaxLength(100);
        builder.Property(a => a.Criteria).HasColumnType("jsonb").IsRequired();
    }
}
