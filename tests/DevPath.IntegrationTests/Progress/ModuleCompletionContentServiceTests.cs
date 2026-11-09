using DevPath.Application.Progress;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using DevPath.Infrastructure.Persistence;
using DevPath.Infrastructure.Progress;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Progress;

/// <summary>IModuleCompletionContentService (#42): só aulas publicadas e prontas, em ordem.</summary>
public sealed class ModuleCompletionContentServiceTests : IAsyncLifetime
{
    private readonly string? _envCs = Environment.GetEnvironmentVariable("DEVPATH_TEST_PG");
    private readonly PostgreSqlContainer? _postgres = Environment.GetEnvironmentVariable("DEVPATH_TEST_PG") is null
        ? new PostgreSqlBuilder("postgres:16-alpine")
            .WithName($"devpath-modsummary-{Guid.NewGuid():N}")
            .Build()
        : null;

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            return;
        }

        // Banco compartilhado via DEVPATH_TEST_PG: limpa o schema para a suíte não herdar dados.
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres is null ? Task.CompletedTask : _postgres.DisposeAsync().AsTask();

    private DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(_envCs ?? _postgres!.GetConnectionString())
            .Options);

    private ModuleCompletionContentService CreateService(DevPathDbContext db) => new(db);

    [Fact]
    public async Task GetAsync_RetornaSubtitleEFrasesPublicadasNaOrdem()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var path = CareerPath.Create($"SQL & Dados {Guid.NewGuid():N}");
        path.Activate();
        path.ChangeSlug("sql-dados-svc");
        var module = path.AddModule("Primeiros passos com SQL");
        module.ChangeSlug("primeiros-passos-com-sql");
        module.Publish();
        module.SetCompletionSubtitle("Subtítulo do módulo.");

        var a = module.AddLesson("A", ContentType.Markdown, "# a");
        a.ChangeSlug("aula-a");
        a.SetCompletionSummary("Frase A");

        var draft = module.AddLesson("Rascunho", ContentType.Markdown, "# r", isDraft: true);
        draft.ChangeSlug("aula-rascunho");
        draft.SetCompletionSummary("Não deve aparecer");

        var unpublished = module.AddLesson("Removida", ContentType.Markdown, "# x");
        unpublished.ChangeSlug("aula-removida");
        unpublished.SetCompletionSummary("Também não");
        unpublished.Unpublish();

        var semFrase = module.AddLesson("Sem frase", ContentType.Markdown, "# s");
        semFrase.ChangeSlug("aula-sem-frase");

        var b = module.AddLesson("B", ContentType.Markdown, "# b");
        b.ChangeSlug("aula-b");
        b.SetCompletionSummary("Frase B");

        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();

        await using var read = CreateDbContext();
        var dto = await CreateService(read).GetAsync("sql-dados-svc", "primeiros-passos-com-sql");

        dto.Should().NotBeNull();
        dto!.Subtitle.Should().Be("Subtítulo do módulo.");
        dto.LearnedByLessonSlug.Keys.Should().Equal("aula-a", "aula-b");
        dto.LearnedByLessonSlug["aula-a"].Should().Be("Frase A");
        dto.LearnedByLessonSlug["aula-b"].Should().Be("Frase B");
    }

    [Fact]
    public async Task GetAsync_TrilhaOuModuloInexistente_RetornaNull()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var service = CreateService(db);
        (await service.GetAsync("nao-existe", "modulo")).Should().BeNull();
        (await service.GetAsync("sql-dados", "nao-existe")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NormalizaSlugs()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var path = CareerPath.Create($"SQL {Guid.NewGuid():N}");
        path.Activate();
        path.ChangeSlug("sql-norm");
        var module = path.AddModule("M");
        module.ChangeSlug("mod-1");
        module.Publish();
        module.SetCompletionSubtitle("Oi");
        var lesson = module.AddLesson("Aula", ContentType.Markdown, "# a");
        lesson.ChangeSlug("aula-1");
        lesson.SetCompletionSummary("Frase");
        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();

        await using var read = CreateDbContext();
        var dto = await CreateService(read).GetAsync("SQL-Norm", "Mod-1");
        dto!.Subtitle.Should().Be("Oi");
        dto.LearnedByLessonSlug.Should().ContainKey("aula-1");
    }
}
