using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class UserProgressConfiguration : IEntityTypeConfiguration<UserProgress>
{
    public const string UniqueLessonIndexName = "IX_UserProgress_UserId_LessonId";

    public void Configure(EntityTypeBuilder<UserProgress> builder)
    {
        builder.ToTable("UserProgress", t =>
        {
            t.HasCheckConstraint("CK_UserProgress_TimeSpentSeconds_NonNegative", "\"TimeSpentSeconds\" >= 0");
            t.HasCheckConstraint("CK_UserProgress_Score_NonNegative", "\"Score\" IS NULL OR \"Score\" >= 0");

            // O progresso é de exatamente UM alvo: aula, exercício ou módulo.
            t.HasCheckConstraint("CK_UserProgress_ExactlyOneTarget", "num_nonnulls(\"LessonId\", \"ExerciseId\", \"ModuleId\") = 1");

            // "Como concluiu" só existe em progresso concluído (ADR-007).
            t.HasCheckConstraint("CK_UserProgress_CompletionKind",
                "\"CompletionKind\" IS NULL OR \"Status\" IN ('Completed', 'Mastered')");
        });
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CompletionKind).HasConversion<string>().HasMaxLength(30);

        // "Qual o progresso deste aluno nesta aula?" é a consulta mais comum (SPEC §4.3).
        // Único (uma linha por aluno e aula): é o alvo do upsert (INSERT ... ON CONFLICT) que torna "abrir" e
        // "concluir" idempotentes com duplo clique, duas abas e o Worker ao mesmo tempo (ADR-007).
        builder.HasIndex(p => new { p.UserId, p.LessonId })
            .IsUnique()
            .HasFilter("\"LessonId\" IS NOT NULL")
            .HasDatabaseName(UniqueLessonIndexName);

        // FKs sem propriedade de navegação: o Domain guarda só os IDs.
        builder.HasOne<Lesson>().WithMany().HasForeignKey(p => p.LessonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Exercise>().WithMany().HasForeignKey(p => p.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(p => p.ModuleId).OnDelete(DeleteBehavior.Cascade);

        // UserId -> AspNetUsers: apagar o usuário apaga o progresso dele (LGPD: dado do aluno sai junto).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
