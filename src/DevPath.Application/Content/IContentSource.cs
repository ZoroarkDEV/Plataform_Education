namespace DevPath.Application.Content;

/// <summary>Fonte do conteúdo pedagógico (hoje: arquivos em <c>/content</c>).</summary>
public interface IContentSource
{
    /// <summary>Lê todas as trilhas.</summary>
    /// <exception cref="ContentValidationException">Quando algum arquivo está faltando ou mal formatado.</exception>
    IReadOnlyList<PathContent> LoadPaths();

    /// <summary>Lê os datasets de exercícios (<c>content/datasets/*.sql</c>). Sem a pasta, lista vazia.</summary>
    IReadOnlyList<DatasetContent> LoadDatasets();
}
