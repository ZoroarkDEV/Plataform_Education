using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Onboarding;

/// <summary>
/// A app inteira com o serviço REAL do Backend (<c>IStudentJourneyService</c>) atrás do adapter da Web,
/// num Postgres próprio (Testcontainers) com trilhas pequenas criadas aqui, sem depender do /content.
/// </summary>
/// <remarks>
/// Trilhas:
/// <list type="bullet">
/// <item>"SQL Teste": 1 módulo publicado com "Primeira Aula", "Segunda Aula" e um rascunho.</item>
/// <item>"Backend Teste": 1 módulo com 1 aula (para a 2ª matrícula em outra trilha).</item>
/// <item>"So Rascunhos": só aula em rascunho (AvailableLessons = 0, TotalLessons = 1): "Em breve" na escolha
/// e a matrícula dá NoLessons.</item>
/// <item>"Sem Aulas": módulo publicado sem nenhuma aula (AvailableLessons = TotalLessons = 0): NoLessons no serviço.</item>
/// </list>
/// </remarks>
public sealed class JourneyWebApplicationFactory : DevPathWebApplicationFactory
{
    public string SqlSlug { get; private set; } = "";
    public string BackendSlug { get; private set; } = "";
    public string DraftsOnlySlug { get; private set; } = "";
    public string NoLessonsSlug { get; private set; } = "";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();

        var sql = CareerPath.Create("SQL Teste", "SQL para os testes.", DifficultyLevel.Beginner, estimatedHours: 10, order: 1);
        var m1 = sql.AddModule("Primeiros passos");
        m1.Publish();
        m1.AddLesson("Primeira Aula", ContentType.Markdown, "# 1");
        m1.AddLesson("Segunda Aula", ContentType.Markdown, "# 2");
        m1.AddLesson("Aula Rascunho", ContentType.Markdown, "# r", isDraft: true);

        var backend = CareerPath.Create("Backend Teste", "C# para os testes.", DifficultyLevel.Intermediate, estimatedHours: 20, order: 2);
        var bm = backend.AddModule("Fundamentos");
        bm.Publish();
        bm.AddLesson("Variaveis", ContentType.Markdown, "# v");

        var drafts = CareerPath.Create("So Rascunhos", "Tudo em construção.", order: 3);
        var dm = drafts.AddModule("Em construção");
        dm.Publish();
        dm.AddLesson("Rascunho Unico", ContentType.Markdown, "# r", isDraft: true);

        var empty = CareerPath.Create("Sem Aulas", "Nada ainda.", order: 4);
        empty.AddModule("Vazio").Publish();

        db.CareerPaths.AddRange(sql, backend, drafts, empty);
        await db.SaveChangesAsync();

        (SqlSlug, BackendSlug, DraftsOnlySlug, NoLessonsSlug) = (sql.Slug, backend.Slug, drafts.Slug, empty.Slug);
    }
}

[CollectionDefinition(Name)]
public sealed class JourneyAppCollection : ICollectionFixture<JourneyWebApplicationFactory>
{
    public const string Name = "JourneyApp";
}
