using DevPath.Domain.Datasets;

namespace DevPath.Application.CodeExecution;

/// <summary>Busca os datasets de exercício (tabela <c>Datasets</c>) pelo slug.</summary>
public interface IDatasetRepository
{
    /// <summary>Nulo quando não existe. Quem precisa do dataset decide se isso é erro (normalmente é).</summary>
    Task<Dataset?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Um caso de teste aponta para um dataset que não está no banco. É erro de CONTEÚDO (o seed não rodou
/// ou o slug está errado), nunca do aluno: a tentativa não é gravada.
/// </summary>
public sealed class DatasetNotFoundException(string slug)
    : Exception($"O dataset '{slug}' não foi encontrado. Rode o seed de conteúdo (content/datasets/{slug}.sql) ou corrija o slug no caso de teste.")
{
    public string Slug { get; } = slug;
}
