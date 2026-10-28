using DevPath.Domain.Common;

namespace DevPath.Application.Content;

// Modelos do conteúdo lido de /content, antes de virar entidade no banco.
// São só dados (records): quem lê os arquivos é a Infrastructure, quem valida é o ContentValidator.

/// <summary>Uma trilha lida de <c>content/paths/&lt;slug&gt;/path.yaml</c>. O slug é o nome da pasta.</summary>
public sealed record PathContent(
    string Slug,
    string Title,
    string Description,
    string? Icon,
    DifficultyLevel DifficultyLevel,
    int EstimatedHours,
    int Order,
    IReadOnlyList<ModuleContent> Modules);

/// <summary>Um módulo listado em <c>path.yaml</c>, com as aulas da pasta <c>modules/&lt;slug&gt;/</c>.</summary>
public sealed record ModuleContent(
    string Slug,
    string Title,
    string Description,
    int Order,
    IReadOnlyList<LessonContent> Lessons);

/// <summary>
/// Uma aula em Markdown: metadados do frontmatter + corpo do texto.
/// O slug vem do nome do arquivo sem o prefixo de ordem (<c>02-joins.md</c> -> <c>joins</c>).
/// </summary>
public sealed record LessonContent(
    string SourceFile,
    string Slug,
    string Title,
    string ModuleSlug,
    int Order,
    int EstimatedMinutes,
    string? Language,
    DifficultyLevel Difficulty,
    string Body);
