using System.Text.Json;
using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
            t.HasCheckConstraint("CK_Exercises_Hints_IsArray", "jsonb_typeof(\"Hints\") = 'array'");
        });
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Statement).IsRequired();
        builder.Property(e => e.Language).HasMaxLength(50).IsRequired();

        // jsonb: o PostgreSQL valida o JSON e permite consultar dentro dele.
        builder.Property(e => e.TestCases).HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.Dataset).HasMaxLength(Exercise.MaxDatasetLength);

        // Dicas: lista de textos em jsonb, na ordem (mais geral -> mais direta). Mesmo esquema de Lesson.LearningObjectives:
        // leitura estrita (item inválido lança JsonException) e comparador por conteúdo.
        builder.Property(e => e.Hints)
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => LearningObjectivesJson.Deserialize(json),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()))
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.HasIndex(e => e.LessonId);
    }
}
