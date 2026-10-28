using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class UserProgressConfiguration : IEntityTypeConfiguration<UserProgress>
{
    public void Configure(EntityTypeBuilder<UserProgress> builder)
    {
        builder.ToTable("UserProgress");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        // "Qual o progresso deste aluno nesta aula?" é a consulta mais comum (SPEC §4.3).
        builder.HasIndex(p => new { p.UserId, p.LessonId });

        // FKs sem propriedade de navegação: o Domain guarda só os IDs.
        builder.HasOne<Lesson>().WithMany().HasForeignKey(p => p.LessonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Exercise>().WithMany().HasForeignKey(p => p.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(p => p.ModuleId).OnDelete(DeleteBehavior.Cascade);

        // UserId: a FK para AspNetUsers entra na issue #9 (ApplicationUser / Identity).
    }
}
