namespace DevPath.Application.Lessons;

/// <summary>
/// Carrega uma aula pelos três slugs da URL <c>/trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}</c>.
/// </summary>
/// <remarks>
/// Regras de visibilidade:
/// <list type="bullet">
/// <item>Trilha inativa, módulo não publicado ou aula despublicada (<c>IsPublished = false</c>) →
/// <see cref="LessonLoadErrorKind.NotFound"/>. A aula despublicada também some da navegação
/// (anterior/próxima, posição e total).</item>
/// <item>Aula em rascunho (<c>IsDraft = true</c>) → <see cref="LessonLoadErrorKind.Draft"/> se aberta
/// direto, mas continua na navegação com <see cref="LessonLink.IsDraft"/> = true.</item>
/// <item>Conteúdo vazio → <see cref="LessonLoadErrorKind.Malformed"/>.</item>
/// </list>
/// </remarks>
public interface ILessonContentService
{
    Task<LessonLoadResult> GetLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);
}
