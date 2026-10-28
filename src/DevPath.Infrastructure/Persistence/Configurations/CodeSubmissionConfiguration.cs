using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class CodeSubmissionConfiguration : IEntityTypeConfiguration<CodeSubmission>
{
    public void Configure(EntityTypeBuilder<CodeSubmission> builder)
    {
        builder.ToTable("CodeSubmissions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SubmittedCode).IsRequired();
        builder.Property(s => s.Result).HasConversion<string>().HasMaxLength(10);

        // Histórico de tentativas de um aluno em um exercício.
        builder.HasIndex(s => new { s.UserId, s.ExerciseId });

        builder.HasOne<Exercise>().WithMany().HasForeignKey(s => s.ExerciseId).OnDelete(DeleteBehavior.Cascade);
    }
}
