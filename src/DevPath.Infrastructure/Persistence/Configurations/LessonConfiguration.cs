using System.Text.Json;
using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
            // Objetivos sempre como lista JSON ([...]); um objeto ou texto solto é barrado no banco.
            t.HasCheckConstraint("CK_Lessons_LearningObjectives_IsArray", "jsonb_typeof(\"LearningObjectives\") = 'array'");
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

        // Objetivos de aprendizagem: lista de textos gravada como jsonb (ex.: ["Usar SELECT", "Filtrar com WHERE"]).
        // O conversor faz lista <-> JSON; o comparador compara o CONTEÚDO da lista, para o EF perceber
        // mudanças (por padrão ele só compararia a referência).
        builder.Property(l => l.LearningObjectives)
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => LearningObjectivesJson.Deserialize(json),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()))
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb") // aulas que já existem no banco ganham lista vazia
            .IsRequired();

        // IsDraft: bool comum. Na migration, as aulas existentes recebem false (defaultValue da coluna).

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

/// <summary>
/// Leitura estrita de listas de textos em jsonb (<c>Lesson.LearningObjectives</c>, <c>Exercise.Hints</c>): só aceita lista de textos não vazios.
/// Qualquer outra coisa (<c>{"a":1}</c>, <c>[1]</c>, <c>["ok", null]</c>) lança <see cref="JsonException"/>,
/// que o <c>LessonContentService</c> transforma em "conteúdo malformado". <c>null</c> em JSON vira lista vazia.
/// </summary>
internal static class LearningObjectivesJson
{
    public static List<string> Deserialize(string json)
    {
        var list = JsonSerializer.Deserialize<List<string?>>(json, (JsonSerializerOptions?)null) ?? [];
        if (list.Any(string.IsNullOrWhiteSpace))
        {
            throw new JsonException("Lista de textos em jsonb tem item nulo ou vazio.");
        }

        return list!;
    }
}
