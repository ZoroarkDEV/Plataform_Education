using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class CareerPathConfiguration : IEntityTypeConfiguration<CareerPath>
{
    public void Configure(EntityTypeBuilder<CareerPath> builder)
    {
        builder.ToTable("CareerPaths");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Icon).HasMaxLength(100);
        builder.Property(p => p.DifficultyLevel).HasConversion<string>().HasMaxLength(20);

        // O slug aparece na URL (/trilhas/backend-net), então não pode repetir.
        builder.HasIndex(p => p.Slug).IsUnique();

        // CareerPath 1:N Module (SPEC §4.2). Apagar a trilha apaga seus módulos.
        builder.HasMany(p => p.Modules)
            .WithOne()
            .HasForeignKey(m => m.CareerPathId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
