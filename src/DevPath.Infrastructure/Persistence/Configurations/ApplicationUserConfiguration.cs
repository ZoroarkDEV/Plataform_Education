using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

/// <summary>Campos extras do usuário na tabela AspNetUsers (o resto é mapeado pelo IdentityDbContext).</summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(10).HasDefaultValue(ApplicationUser.DefaultLanguage);

        // Se a trilha atual for apagada, o usuário continua, só sem trilha atual.
        builder.HasOne<CareerPath>()
            .WithMany()
            .HasForeignKey(u => u.CurrentPathId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
