using DevPath.Application.Content;
using DevPath.Infrastructure.Content;
using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Content;

/// <summary>
/// Roda o ContentSeeder contra um PostgreSQL 16 descartável (Testcontainers) com o /content real.
/// Precisa do Docker rodando na máquina.
/// </summary>
public sealed class ContentSeederTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly string _repoContent = ContentRootLocator.Resolve(null, AppContext.BaseDirectory);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SeedAsync_BancoVazio_CriaAsDuasTrilhasComModulosEAulas()
    {
        var result = await SeedAsync(_repoContent);

        result.Inserted.Should().BeGreaterThan(0);

        await using var db = CreateDbContext();
        var paths = await db.CareerPaths.Include(p => p.Modules).ThenInclude(m => m.Lessons)
            .OrderBy(p => p.Order).ToListAsync();

        paths.Select(p => p.Slug).Should().Equal("backend-net", "sql-dados");
        paths[0].Modules.Should().HaveCount(7).And.OnlyContain(m => m.IsPublished && m.Lessons.Count > 0);
        paths[1].Modules.Should().HaveCount(6);
        paths.SelectMany(p => p.Modules).SelectMany(m => m.Lessons)
            .Should().OnlyContain(l => l.Content.Length > 0 && !l.Content.StartsWith("---"));
    }

    [Fact]
    public async Task SeedAsync_RodandoDuasVezes_NaoDuplicaNemAltera()
    {
        await SeedAsync(_repoContent);
        var before = await CountRowsAsync();

        var second = await SeedAsync(_repoContent);

        second.NothingChanged.Should().BeTrue("o segundo seed não deveria inserir nem atualizar nada");
        (await CountRowsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task SeedAsync_AulaEditada_AtualizaALinhaExistente()
    {
        var copy = CopyContentToTemp();
        try
        {
            await SeedAsync(copy);
            var before = await CountRowsAsync();

            var lessonFile = Path.Combine(copy, "paths", "sql-dados", "modules", "crud", "01-crud-completo.md");
            File.WriteAllText(lessonFile, File.ReadAllText(lessonFile)
                .Replace("title: \"CRUD completo\"", "title: \"CRUD completo (revisado)\""));

            var result = await SeedAsync(copy);

            result.Should().Be(new ContentSeedResult(Inserted: 0, Updated: 1));
            (await CountRowsAsync()).Should().Be(before);
            await using var db = CreateDbContext();
            (await db.Lessons.CountAsync(l => l.Title == "CRUD completo (revisado)")).Should().Be(1);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public async Task SeedAsync_ConteudoInvalido_NaoGravaNada()
    {
        var copy = CopyContentToTemp();
        try
        {
            var lessonFile = Path.Combine(copy, "paths", "sql-dados", "modules", "crud", "01-crud-completo.md");
            File.WriteAllText(lessonFile, File.ReadAllText(lessonFile).Replace("order: 1", "order: 5"));

            var act = () => SeedAsync(copy);

            await act.Should().ThrowAsync<ContentValidationException>();
            (await CountRowsAsync()).Should().Be((0, 0, 0));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private async Task<ContentSeedResult> SeedAsync(string contentRoot)
    {
        await using var db = CreateDbContext();
        var seeder = new ContentSeeder(db, new FileSystemContentSource(contentRoot), NullLogger<ContentSeeder>.Instance);
        return await seeder.SeedAsync();
    }

    private async Task<(int Paths, int Modules, int Lessons)> CountRowsAsync()
    {
        await using var db = CreateDbContext();
        return (await db.CareerPaths.CountAsync(), await db.Modules.CountAsync(), await db.Lessons.CountAsync());
    }

    private string CopyContentToTemp()
    {
        var target = Directory.CreateTempSubdirectory("devpath-content-").FullName;
        foreach (var file in Directory.GetFiles(_repoContent, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(_repoContent, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    private DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
