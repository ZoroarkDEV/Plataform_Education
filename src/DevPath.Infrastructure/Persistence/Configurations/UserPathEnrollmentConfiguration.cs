using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class UserPathEnrollmentConfiguration : IEntityTypeConfiguration<UserPathEnrollment>
{
    public void Configure(EntityTypeBuilder<UserPathEnrollment> builder)
    {
        builder.ToTable("UserPathEnrollments");

        // Chave composta: um aluno se matricula uma única vez em cada trilha.
        builder.HasKey(e => new { e.UserId, e.CareerPathId });

        builder.Property(e => e.ProgressPercentage).HasPrecision(5, 2); // 0.00 a 100.00

        builder.HasOne<CareerPath>().WithMany().HasForeignKey(e => e.CareerPathId).OnDelete(DeleteBehavior.Cascade);

        // Se o módulo atual for apagado, a matrícula continua, só sem módulo atual.
        builder.HasOne<Module>().WithMany().HasForeignKey(e => e.CurrentModuleId).OnDelete(DeleteBehavior.SetNull);
    }
}
