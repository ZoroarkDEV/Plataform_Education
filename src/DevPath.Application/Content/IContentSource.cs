namespace DevPath.Application.Content;

/// <summary>Fonte do conteúdo pedagógico (hoje: arquivos em <c>/content</c>).</summary>
public interface IContentSource
{
    /// <summary>Lê todas as trilhas.</summary>
    /// <exception cref="ContentValidationException">Quando algum arquivo está faltando ou mal formatado.</exception>
    IReadOnlyList<PathContent> LoadPaths();
}
