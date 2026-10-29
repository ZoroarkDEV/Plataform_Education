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
            t.HasCheckConstraint("CK_Exercises_CommonErrors_IsArray", "jsonb_typeof(\"CommonErrors\") = 'array'");
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

        // Erros comuns: [{"example": "...", "message": "..."}] em jsonb. Só para o feedback no servidor (nunca vai para DTO).
        // Mesmo esquema das dicas: leitura estrita e comparador por conteúdo (o record compara por valor).
        builder.Property(e => e.CommonErrors)
            .HasConversion(
                list => JsonSerializer.Serialize(list, CommonErrorsJson.Options),
                json => CommonErrorsJson.Deserialize(json),
                new ValueComparer<IReadOnlyList<CommonError>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()))
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        // IsPublished: SEM HasDefaultValue(true) no modelo, pelo mesmo motivo de Lesson.IsPublished (o EF não
        // mandaria o false no INSERT). O default true fica só na migration, para os exercícios que já existem.
        builder.Property(e => e.IsPublished).IsRequired();

        builder.HasIndex(e => e.LessonId);
    }
}

/// <summary>
/// Leitura estrita de <c>Exercise.CommonErrors</c>: lista de objetos com <c>example</c> e <c>message</c> não vazios.
/// Qualquer outra coisa lança <see cref="JsonException"/>. <c>null</c> em JSON vira lista vazia.
/// </summary>
internal static class CommonErrorsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<CommonError> Deserialize(string json)
    {
        var list = JsonSerializer.Deserialize<List<CommonError?>>(json, Options) ?? [];
        if (list.Any(e => e is null || string.IsNullOrWhiteSpace(e.Example) || string.IsNullOrWhiteSpace(e.Message)))
        {
            throw new JsonException("Lista de erros comuns em jsonb tem item nulo ou sem example/message.");
        }

        return list!;
    }
}
