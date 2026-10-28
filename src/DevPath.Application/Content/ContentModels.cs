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
    string Body)
{
    /// <summary>
    /// "O que você vai aprender": frases curtas do frontmatter (<c>objectives:</c>).
    /// O seed grava em <c>Lesson.LearningObjectives</c>.
    /// </summary>
    public IReadOnlyList<string> Objectives { get; init; } = [];

    /// <summary>Rascunho (<c>draft: true</c>): aparece como "Em breve", não conta no progresso.</summary>
    public bool IsDraft { get; init; }

    /// <summary>Exercício da aula (<c>NN-slug.exercise.json</c> ao lado do .md), se houver.</summary>
    public ExerciseContent? Exercise { get; init; }
}

/// <summary>
/// Exercício de uma aula, lido de <c>NN-slug.exercise.json</c>. A solução fica só aqui (e no banco),
/// nunca no Markdown da aula: é a "solução protegida" do ADR-003.
/// </summary>
public sealed record ExerciseContent(
    string SourceFile,
    string Title,
    string Statement,
    string Language,
    string? Dataset,
    int Points,
    string StarterCode,
    string Solution,
    IReadOnlyList<string> Hints,
    IReadOnlyList<ExerciseTestCase> TestCases,
    IReadOnlyList<ExerciseCommonError> CommonErrors,
    string TestCasesJson);

/// <summary>
/// Resultado esperado de uma consulta. Compara-se o resultado, não o texto da consulta:
/// mesma quantidade e ordem de colunas, mesmas linhas, números pelo valor, textos exatos.
/// </summary>
/// <param name="Columns">Nomes das colunas esperadas (só são cobrados se <paramref name="CheckColumnNames"/>).</param>
/// <param name="Rows">Linhas esperadas; cada valor é string, decimal, bool ou null.</param>
/// <param name="OrderMatters">A ordem das linhas só conta quando o enunciado pede ordenação.</param>
public sealed record ExerciseTestCase(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool OrderMatters,
    bool CheckColumnNames);

/// <summary>Erro comum de iniciante e a mensagem amigável para ele (o exemplo nunca pode passar no teste).</summary>
public sealed record ExerciseCommonError(string Example, string Message);
