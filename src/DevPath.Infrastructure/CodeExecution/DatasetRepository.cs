using DevPath.Application.CodeExecution;
using DevPath.Domain.Datasets;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>Implementação com EF Core. O índice único em <c>Slug</c> deixa a busca direta.</summary>
public sealed class DatasetRepository(DevPathDbContext db) : IDatasetRepository
{
    public Task<Dataset?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var normalized = slug.Trim().ToLowerInvariant();
        return db.Datasets.AsNoTracking().SingleOrDefaultAsync(d => d.Slug == normalized, cancellationToken);
    }
}
