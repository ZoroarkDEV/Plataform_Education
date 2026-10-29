using DevPath.Domain.Datasets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPath.Infrastructure.Persistence.Configurations;

internal sealed class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> builder)
    {
        builder.ToTable("Datasets");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => d.Slug).IsUnique(); // os casos de teste acham o dataset pelo slug

        builder.Property(d => d.Sql).HasColumnType("text").IsRequired();
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
    }
}
