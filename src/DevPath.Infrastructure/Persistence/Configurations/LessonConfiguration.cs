using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Title).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Content).IsRequired(); // text, sem limite
        builder.Property(l => l.CodeLanguage).HasMaxLength(50);

        // Enums gravados como texto: "Markdown" é mais legível no banco do que 1.
        builder.Property(l => l.ContentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Difficulty).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(l => new { l.ModuleId, l.Order });

        // Lesson 1:N Exercise
        builder.HasMany(l => l.Exercises)
            .WithOne()
            .HasForeignKey(e => e.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // TODO (SPEC §4.3): busca full-text em Content com tsvector, numa migration futura.
    }
}
