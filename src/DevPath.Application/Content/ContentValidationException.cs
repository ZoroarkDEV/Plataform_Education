namespace DevPath.Application.Content;

/// <summary>Conteúdo inválido em <c>/content</c>. A mensagem lista todos os problemas encontrados.</summary>
public sealed class ContentValidationException(IReadOnlyList<string> errors)
    : Exception("Conteúdo inválido em /content:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
