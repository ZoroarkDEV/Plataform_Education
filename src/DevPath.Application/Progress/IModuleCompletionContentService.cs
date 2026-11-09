namespace DevPath.Application.Progress;

/// <summary>
/// Textos da tela "Módulo concluído" (#42): subtítulo do módulo e frases de "O que você aprendeu"
/// por aula, lidos do banco (seed de <c>completion_subtitle</c> / <c>completion_summary</c>).
/// </summary>
/// <param name="Subtitle">Subtítulo do módulo; <c>null</c> = esconder a seção.</param>
/// <param name="LearnedByLessonSlug">
/// Frase por slug de aula. Só entram aulas publicadas, não-rascunho e com frase preenchida,
/// na ordem do módulo. Aula sem frase não aparece.
/// </param>
public sealed record ModuleCompletionContentDto(
    string? Subtitle,
    IReadOnlyDictionary<string, string> LearnedByLessonSlug)
{
    public static readonly ModuleCompletionContentDto Empty =
        new(null, new Dictionary<string, string>());
}

/// <summary>
/// Porta de leitura dos textos de módulo concluído. A Web adapta para
/// <c>IModuleCompletionContentSource</c> (branch da UI) mapeando este DTO.
/// </summary>
public interface IModuleCompletionContentService
{
    /// <summary>
    /// Textos do módulo na trilha. <c>null</c> se a trilha/módulo não existir (ou módulo sem aulas
    /// publicadas prontas — a UI já trata "não encontrado" pelo dashboard; aqui devolve Empty
    /// com subtítulo nulo e dicionário vazio quando o módulo existe mas sem frases).
    /// </summary>
    Task<ModuleCompletionContentDto?> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken = default);
}
