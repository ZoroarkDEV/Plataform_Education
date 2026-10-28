using DevPath.Domain.Common;

namespace DevPath.Application.Lessons;

/// <summary>
/// Tudo o que a página da aula precisa, já pronto para exibir. A Web só converte
/// <see cref="MarkdownBody"/> em HTML; ela não consulta o banco.
/// </summary>
/// <param name="Position">Posição da aula no módulo, começando em 1 (ex.: "Aula 2 de 5").</param>
/// <param name="TotalInModule">Quantas aulas publicadas o módulo tem (rascunhos contam).</param>
/// <param name="PreviousLesson">Aula anterior no mesmo módulo, ou <c>null</c> se esta é a primeira.</param>
/// <param name="NextLesson">Próxima aula no mesmo módulo, ou <c>null</c> se esta é a última.</param>
public sealed record LessonDto(
    Guid LessonId,
    string Slug,
    string Title,
    int EstimatedMinutes,
    DifficultyLevel Level,
    string? Language,
    IReadOnlyList<string> LearningObjectives,
    string MarkdownBody,
    LessonBreadcrumb Breadcrumb,
    LessonLink? PreviousLesson,
    LessonLink? NextLesson,
    int Position = 0,
    int TotalInModule = 0);

/// <summary>Trilha › Módulo: os links do "você está aqui" no topo da aula.</summary>
public sealed record LessonBreadcrumb(string PathSlug, string PathTitle, string ModuleSlug, string ModuleTitle);

/// <summary>Link para uma aula vizinha. <paramref name="IsDraft"/> = mostrar "Em breve" em vez de link.</summary>
public sealed record LessonLink(string Slug, string Title, bool IsDraft = false);

public enum LessonLoadErrorKind
{
    /// <summary>Trilha, módulo ou aula não existe ou não está publicado: a Web responde 404.</summary>
    NotFound,

    /// <summary>A aula existe mas o conteúdo está vazio: erro nosso, não do aluno.</summary>
    Malformed,

    /// <summary>Aula em construção: aparece na navegação, mas ainda não abre.</summary>
    Draft
}

/// <param name="Detail">Detalhe técnico para log e para "Ver detalhe técnico". O texto amigável ao aluno é escolhido pela Web a partir de <see cref="Kind"/>.</param>
public sealed record LessonLoadError(LessonLoadErrorKind Kind, string Detail);

/// <summary>
/// Resultado de carregar uma aula: ou <see cref="Lesson"/>, ou <see cref="Error"/>, nunca os dois.
/// </summary>
/// <remarks>
/// Por que não lançar exceção? "Aula não encontrada" é um caso esperado (link velho, slug digitado
/// errado), não uma falha do sistema. Um resultado explícito obriga quem chama a tratar cada caso.
/// </remarks>
public sealed record LessonLoadResult
{
    public LessonDto? Lesson { get; }
    public LessonLoadError? Error { get; }
    public bool IsSuccess => Lesson is not null;

    private LessonLoadResult(LessonDto? lesson, LessonLoadError? error)
    {
        Lesson = lesson;
        Error = error;
    }

    public static LessonLoadResult Success(LessonDto lesson) => new(lesson ?? throw new ArgumentNullException(nameof(lesson)), null);

    public static LessonLoadResult NotFound(string detail) => new(null, new LessonLoadError(LessonLoadErrorKind.NotFound, detail));

    public static LessonLoadResult Malformed(string detail) => new(null, new LessonLoadError(LessonLoadErrorKind.Malformed, detail));

    public static LessonLoadResult Draft(string detail) => new(null, new LessonLoadError(LessonLoadErrorKind.Draft, detail));

    /// <summary>Forma genérica (compatível com o contrato combinado com o Frontend em feat/aulas-markdown).</summary>
    public static LessonLoadResult Failure(LessonLoadErrorKind kind, string detail) => new(null, new LessonLoadError(kind, detail));
}
