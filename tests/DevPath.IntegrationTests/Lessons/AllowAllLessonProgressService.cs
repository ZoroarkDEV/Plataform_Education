using DevPath.Application.Progress;

namespace DevPath.IntegrationTests.Lessons;

/// <summary>
/// Dublê do progresso para os testes da TELA com o conteúdo em memória (o banco de teste não tem essas aulas):
/// libera tudo e não grava nada. O acesso de verdade é testado em LessonAccessPageTests, com o serviço real.
/// </summary>
public sealed class AllowAllLessonProgressService : ILessonProgressService
{
    public Task<LessonAccessResult> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
        Task.FromResult(new LessonAccessResult(LessonAccessKind.Allowed));

    public Task<LessonAccessResult> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
        Task.FromResult(new LessonAccessResult(LessonAccessKind.Allowed));

    public Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
        throw new NotSupportedException("A tela de leitura não conclui aulas.");
}
