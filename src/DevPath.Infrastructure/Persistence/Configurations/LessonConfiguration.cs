using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons", t =>
        {
            t.HasCheckConstraint("CK_Lessons_Order_NonNegative", "\"Order\" >= 0");
            t.HasCheckConstraint("CK_Lessons_EstimatedMinutes_NonNegative", "\"EstimatedMinutes\" >= 0");
        });
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Title).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Slug).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Content).IsRequired(); // text, sem limite
        builder.Property(l => l.CodeLanguage).HasMaxLength(50);

        // Enums gravados como texto: "Markdown" é mais legível no banco do que 1.
        builder.Property(l => l.ContentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Difficulty).HasConversion<string>().HasMaxLength(20);

        // IsPublished: SEM HasDefaultValue(true) no modelo, de propósito. Com ele, o EF não enviaria o
        // valor false no INSERT (false é o valor padrão do bool) e o banco gravaria true: uma aula
        // despublicada nasceria publicada. O default true fica só na migration, para as aulas existentes.
        builder.Property(l => l.IsPublished).IsRequired();

        // Único: duas aulas não podem ocupar a mesma posição no módulo.
        builder.HasIndex(l => new { l.ModuleId, l.Order }).IsUnique();

        // O slug identifica a aula dentro do módulo.
        builder.HasIndex(l => new { l.ModuleId, l.Slug }).IsUnique();

        // Lesson 1:N Exercise
        builder.HasMany(l => l.Exercises)
            .WithOne()
            .HasForeignKey(e => e.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // TODO (SPEC §4.3): busca full-text em Content com tsvector, numa migration futura.
    }
}
