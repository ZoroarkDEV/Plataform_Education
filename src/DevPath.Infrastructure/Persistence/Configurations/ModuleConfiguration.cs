using DevPath.Domain.CareerPaths;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("Modules", t =>
        {
            t.HasCheckConstraint("CK_Modules_Order_NonNegative", "\"Order\" >= 0");
            t.HasCheckConstraint("CK_Modules_EstimatedMinutes_NonNegative", "\"EstimatedMinutes\" >= 0");
        });
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Slug).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(2000);

        // Listar os módulos de uma trilha em ordem (SPEC §4.3). Único: dois módulos
        // não podem ocupar a mesma posição. (Para reordenar, troque as posições numa transação
        // passando por um valor temporário, ou torne o índice DEFERRABLE numa migration futura.)
        builder.HasIndex(m => new { m.CareerPathId, m.Order }).IsUnique();

        // O slug identifica o módulo dentro da trilha (o seed usa ele para atualizar em vez de duplicar).
        builder.HasIndex(m => new { m.CareerPathId, m.Slug }).IsUnique();

        // Module 1:N Lesson
        builder.HasMany(m => m.Lessons)
            .WithOne()
            .HasForeignKey(l => l.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
