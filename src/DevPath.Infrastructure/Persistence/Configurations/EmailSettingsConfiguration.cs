using DevPath.Infrastructure.Email;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class EmailSettingsConfiguration : IEntityTypeConfiguration<EmailSettingsRecord>
{
    public void Configure(EntityTypeBuilder<EmailSettingsRecord> builder)
    {
        builder.ToTable("EmailSettings", table =>
        {
            // Uma linha só, e só os provedores conhecidos.
            table.HasCheckConstraint("CK_EmailSettings_Singleton", $"\"Id\" = {EmailSettingsRecord.SingletonId}");
            table.HasCheckConstraint("CK_EmailSettings_Provider", "\"Provider\" IN ('None', 'Smtp', 'Resend')");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Provider).HasMaxLength(16).IsRequired();
        builder.Property(s => s.FromAddress).HasMaxLength(254).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ResendApiKeyProtected);
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Auditoria: apagar a conta de quem salvou não apaga a configuração.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
