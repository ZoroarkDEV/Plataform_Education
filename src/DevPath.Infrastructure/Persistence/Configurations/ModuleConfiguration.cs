using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("Modules");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(2000);

        // Listar os módulos de uma trilha em ordem (SPEC §4.3).
        builder.HasIndex(m => new { m.CareerPathId, m.Order });

        // Module 1:N Lesson
        builder.HasMany(m => m.Lessons)
            .WithOne()
            .HasForeignKey(l => l.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
