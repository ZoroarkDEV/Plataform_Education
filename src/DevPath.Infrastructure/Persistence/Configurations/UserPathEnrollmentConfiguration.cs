using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class UserPathEnrollmentConfiguration : IEntityTypeConfiguration<UserPathEnrollment>
{
    public void Configure(EntityTypeBuilder<UserPathEnrollment> builder)
    {
        builder.ToTable("UserPathEnrollments", t =>
            t.HasCheckConstraint("CK_UserPathEnrollments_ProgressPercentage_Range", "\"ProgressPercentage\" BETWEEN 0 AND 100"));

        // Chave composta: um aluno se matricula uma única vez em cada trilha.
        builder.HasKey(e => new { e.UserId, e.CareerPathId });

        builder.Property(e => e.ProgressPercentage).HasPrecision(5, 2); // 0.00 a 100.00

        builder.HasOne<CareerPath>().WithMany().HasForeignKey(e => e.CareerPathId).OnDelete(DeleteBehavior.Cascade);

        // Se o módulo atual for apagado, a matrícula continua, só sem módulo atual.
        builder.HasOne<Module>().WithMany().HasForeignKey(e => e.CurrentModuleId).OnDelete(DeleteBehavior.SetNull);

        // UserId -> AspNetUsers: apagar o usuário apaga as matrículas dele (LGPD: dado do aluno sai junto).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
