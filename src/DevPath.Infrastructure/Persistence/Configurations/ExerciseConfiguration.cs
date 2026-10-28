using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("Exercises", t =>
        {
            t.HasCheckConstraint("CK_Exercises_Points_NonNegative", "\"Points\" >= 0");
            t.HasCheckConstraint("CK_Exercises_MaxAttempts_Positive", "\"MaxAttempts\" IS NULL OR \"MaxAttempts\" > 0");
        });
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Statement).IsRequired();
        builder.Property(e => e.Language).HasMaxLength(50).IsRequired();

        // jsonb: o PostgreSQL valida o JSON e permite consultar dentro dele.
        builder.Property(e => e.TestCases).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(e => e.LessonId);
    }
}
