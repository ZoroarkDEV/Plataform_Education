using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class CodeSubmissionConfiguration : IEntityTypeConfiguration<CodeSubmission>
{
    public void Configure(EntityTypeBuilder<CodeSubmission> builder)
    {
        builder.ToTable("CodeSubmissions", t =>
        {
            t.HasCheckConstraint("CK_CodeSubmissions_ExecutionTimeMs_NonNegative", "\"ExecutionTimeMs\" >= 0");
            // #43: o detalhe (Outcome) não pode contradizer o Result que as regras de tentativa usam.
            t.HasCheckConstraint("CK_CodeSubmissions_Outcome_MatchesResult",
                "\"Outcome\" IS NULL OR ((\"Outcome\" = 'Pass') = (\"Result\" = 'Pass'))");
        });
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SubmittedCode).IsRequired();
        builder.Property(s => s.Result).HasConversion<string>().HasMaxLength(10);
        // Certo/errado/erro/timeout (#43). Nulo só em tentativas antigas que não passaram (antes do #43).
        builder.Property(s => s.Outcome).HasConversion<string>().HasMaxLength(10);

        // Histórico de tentativas de um aluno em um exercício.
        builder.HasIndex(s => new { s.UserId, s.ExerciseId });

        builder.HasOne<Exercise>().WithMany().HasForeignKey(s => s.ExerciseId).OnDelete(DeleteBehavior.Cascade);

        // UserId -> AspNetUsers: apagar o usuário apaga as submissões dele (LGPD: dado do aluno sai junto).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
