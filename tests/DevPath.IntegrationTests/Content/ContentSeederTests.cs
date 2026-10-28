using System.Text.RegularExpressions;
using DevPath.Application.Content;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
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
        paths[1].Modules.Should().HaveCount(7);
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

    [Fact]
    public async Task SeedAsync_AulaNovaNoInicio_ReordenaSemPerderOProgresso()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var joinsId = await LessonIdAsync("consultas", "joins");
            var userId = Guid.NewGuid();
            await using (var db = CreateDbContext())
            {
                // UserProgress.UserId tem FK para AspNetUsers (Identity).
                db.Users.Add(new ApplicationUser { Id = userId, UserName = "aluno@teste.dev", Email = "aluno@teste.dev", DisplayName = "Aluno" });
                db.UserProgress.Add(UserProgress.StartLesson(userId, joinsId));
                await db.SaveChangesAsync();
            }

            // Nova aula 01 empurra as outras: 01-joins -> 02-joins, 02 -> 03, 03 -> 04.
            var module = ModuleDir(copy, "sql-dados", "consultas");
            RenumberLesson(module, "03-subqueries-e-ctes.md", 4);
            RenumberLesson(module, "02-agregacoes-group-by-having.md", 3);
            RenumberLesson(module, "01-joins.md", 2);
            File.WriteAllText(Path.Combine(module, "01-select-basico.md"),
                "---\ntitle: \"SELECT básico\"\nmodule: consultas\norder: 1\nestimated_minutes: 20\nlanguage: sql\ndifficulty: beginner\n---\n\n# SELECT\n\nTexto.\n");

            var result = await SeedAsync(copy);

            // 1 aula nova; 3 aulas mudaram de ordem e o módulo mudou o tempo estimado.
            result.Should().Be(new ContentSeedResult(Inserted: 1, Updated: 4));
            (await LessonSlugsInOrderAsync("consultas")).Should().Equal(
                "select-basico", "joins", "agregacoes-group-by-having", "subqueries-e-ctes");
            (await LessonIdAsync("consultas", "joins")).Should().Be(joinsId);

            await using var check = CreateDbContext();
            var progress = await check.UserProgress.SingleAsync(p => p.UserId == userId);
            var lesson = await check.Lessons.SingleAsync(l => l.Id == progress.LessonId);
            lesson.Slug.Should().Be("joins");
            lesson.Title.Should().StartWith("JOINs");

            (await SeedAsync(copy)).NothingChanged.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SeedAsync_ModulosTrocamDeOrdem_MantemOsMesmosIds()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var ids = await ModuleIdsBySlugAsync("sql-dados");

            EditPathYaml(copy, "sql-dados", yaml => yaml
                .Replace("description: \"CREATE DATABASE, CREATE TABLE e constraints.\"\n    order: 2",
                         "description: \"CREATE DATABASE, CREATE TABLE e constraints.\"\n    order: 3")
                .Replace("description: \"INSERT, SELECT, UPDATE e DELETE.\"\n    order: 3",
                         "description: \"INSERT, SELECT, UPDATE e DELETE.\"\n    order: 2"));

            var result = await SeedAsync(copy);

            result.Should().Be(new ContentSeedResult(Inserted: 0, Updated: 2));
            (await ModuleIdsBySlugAsync("sql-dados")).Should().BeEquivalentTo(ids);
            await using var db = CreateDbContext();
            (await db.Modules.Where(m => m.Slug == "crud" || m.Slug == "estruturas")
                    .OrderBy(m => m.Order).Select(m => m.Slug).ToListAsync())
                .Should().Equal("crud", "estruturas");
        });
    }

    [Fact]
    public async Task SeedAsync_TituloDaTrilhaRenomeado_AtualizaAMesmaTrilha()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            EditPathYaml(copy, "sql-dados", yaml => yaml.Replace("title: \"SQL & Dados\"", "title: \"SQL e Bancos de Dados\""));

            var result = await SeedAsync(copy);

            result.Should().Be(new ContentSeedResult(Inserted: 0, Updated: 1));
            await using var db = CreateDbContext();
            var paths = await db.CareerPaths.OrderBy(p => p.Order).ToListAsync();
            paths.Should().HaveCount(2).And.OnlyContain(p => p.IsActive);
            paths[1].Slug.Should().Be("sql-dados");
            paths[1].Title.Should().Be("SQL e Bancos de Dados");
        });
    }

    [Fact]
    public async Task SeedAsync_ItensRemovidosDoConteudo_DespublicaDesativaENaoApaga()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var before = await CountRowsAsync();

            // Remove o módulo "crud" (e renumera os seguintes), a aula 02 de "consultas" e a trilha backend-net inteira.
            EditPathYaml(copy, "sql-dados", yaml => RemoveModuleEntry(yaml, "crud"));
            Directory.Delete(ModuleDir(copy, "sql-dados", "crud"), recursive: true);
            var consultas = ModuleDir(copy, "sql-dados", "consultas");
            File.Delete(Path.Combine(consultas, "02-agregacoes-group-by-having.md"));
            RenumberLesson(consultas, "03-subqueries-e-ctes.md", 2);
            Directory.Delete(Path.Combine(copy, "paths", "backend-net"), recursive: true);
            EditPathYaml(copy, "sql-dados", yaml => yaml.Replace("order: 2\n\n#", "order: 1\n\n#"));

            await SeedAsync(copy);

            (await CountRowsAsync()).Should().Be(before, "o seed nunca apaga");
            await using (var db = CreateDbContext())
            {
                (await db.CareerPaths.SingleAsync(p => p.Slug == "backend-net")).IsActive.Should().BeFalse();
                (await db.CareerPaths.SingleAsync(p => p.Slug == "sql-dados")).IsActive.Should().BeTrue();

                var modules = await db.Modules.Where(m => db.CareerPaths.Any(p => p.Id == m.CareerPathId && p.Slug == "sql-dados")).OrderBy(m => m.Order).ToListAsync();
                modules.Select(m => (m.Slug, m.Order, m.IsPublished)).Should().Equal(
                    ("ambiente", 1, true), ("estruturas", 2, true), ("consultas", 3, true),
                    ("performance-e-confiabilidade", 4, true), ("conectando-csharp", 5, true),
                    ("primeiros-passos-com-sql", 6, true), ("crud", 7, false));
            }

            (await LessonSlugsInOrderAsync("consultas")).Should().Equal("joins", "subqueries-e-ctes", "agregacoes-group-by-having");
            (await LessonPublishedAsync("consultas", "agregacoes-group-by-having")).Should().BeFalse();
            (await SeedAsync(copy)).NothingChanged.Should().BeTrue();

            // Conteúdo de volta: tudo é republicado/reativado nas posições originais.
            Directory.Delete(copy, recursive: true);
            CopyDirectory(_repoContent, copy);
            await SeedAsync(copy);

            await using var after = CreateDbContext();
            (await after.CareerPaths.CountAsync(p => p.IsActive)).Should().Be(2);
            (await after.Modules.CountAsync(m => !m.IsPublished)).Should().Be(0);
            (await after.Modules.SingleAsync(m => m.Slug == "crud")).Order.Should().Be(3);
            (await LessonSlugsInOrderAsync("consultas")).Should().Equal("joins", "agregacoes-group-by-having", "subqueries-e-ctes");
            (await after.Lessons.CountAsync(l => !l.IsPublished)).Should().Be(0);
        });
    }

    [Fact]
    public async Task SeedAsync_AulaRemovidaDoConteudo_EhDespublicadaEMantemOProgresso()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var lessonId = await LessonIdAsync("ambiente", "instalando-docker-desktop");
            var userId = await AddProgressAsync(lessonId);

            // Remove a aula 02 e renumera as seguintes para 2..3.
            var ambiente = ModuleDir(copy, "sql-dados", "ambiente");
            File.Delete(Path.Combine(ambiente, "02-instalando-docker-desktop.md"));
            RenumberLesson(ambiente, "03-docker-compose-com-postgresql.md", 2);
            RenumberLesson(ambiente, "04-pgadmin-e-dbeaver.md", 3);

            await SeedAsync(copy);

            (await LessonSlugsInOrderAsync("ambiente")).Should().Equal(
                "o-que-e-banco-relacional", "docker-compose-com-postgresql", "pgadmin-e-dbeaver", "instalando-docker-desktop");
            await using (var db = CreateDbContext())
            {
                var removed = await db.Lessons.SingleAsync(l => l.Id == lessonId);
                removed.IsPublished.Should().BeFalse("aula que sumiu de /content não pode continuar visível");
                removed.Order.Should().Be(4);
                (await db.Lessons.CountAsync(l => !l.IsPublished)).Should().Be(1, "só a aula removida é despublicada");
                (await db.UserProgress.SingleAsync(p => p.UserId == userId)).LessonId.Should().Be(lessonId);
            }

            (await SeedAsync(copy)).NothingChanged.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SeedAsync_AulaRemovidaQueVoltaParaOConteudo_EhRepublicadaNaMesmaLinha()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var lessonId = await LessonIdAsync("ambiente", "instalando-docker-desktop");
            var userId = await AddProgressAsync(lessonId);

            var ambiente = ModuleDir(copy, "sql-dados", "ambiente");
            var removedFile = Path.Combine(ambiente, "02-instalando-docker-desktop.md");
            var original = File.ReadAllText(removedFile);
            File.Delete(removedFile);
            RenumberLesson(ambiente, "03-docker-compose-com-postgresql.md", 2);
            RenumberLesson(ambiente, "04-pgadmin-e-dbeaver.md", 3);
            await SeedAsync(copy);
            (await LessonPublishedAsync("ambiente", "instalando-docker-desktop")).Should().BeFalse();

            // A aula volta para a posição original.
            RenumberLesson(ambiente, "03-pgadmin-e-dbeaver.md", 4);
            RenumberLesson(ambiente, "02-docker-compose-com-postgresql.md", 3);
            File.WriteAllText(removedFile, original);

            var result = await SeedAsync(copy);

            result.Inserted.Should().Be(0, "a aula volta na mesma linha, não é criada de novo");
            (await LessonSlugsInOrderAsync("ambiente")).Should().Equal(
                "o-que-e-banco-relacional", "instalando-docker-desktop", "docker-compose-com-postgresql", "pgadmin-e-dbeaver");
            await using (var db = CreateDbContext())
            {
                var back = await db.Lessons.SingleAsync(l => l.Slug == "instalando-docker-desktop");
                back.Id.Should().Be(lessonId);
                back.IsPublished.Should().BeTrue();
                back.Order.Should().Be(2);
                (await db.Lessons.CountAsync(l => !l.IsPublished)).Should().Be(0);
                (await db.UserProgress.SingleAsync(p => p.UserId == userId)).LessonId.Should().Be(lessonId);
            }

            (await SeedAsync(copy)).NothingChanged.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SeedAsync_YamlInvalidoDepoisDeUmSeed_NaoAlteraOBanco()
    {
        await WithContentCopy(async copy =>
        {
            await SeedAsync(copy);
            var before = await SnapshotAsync();

            var module = ModuleDir(copy, "sql-dados", "consultas");
            RenumberLesson(module, "01-joins.md", 9); // muda algo válido...
            File.WriteAllText(Path.Combine(module, "02-agregacoes-group-by-having.md"),
                File.ReadAllText(Path.Combine(module, "02-agregacoes-group-by-having.md")).Replace("order: 2", "ordem: 2")); // ...e quebra outro

            var act = () => SeedAsync(copy);

            (await act.Should().ThrowAsync<ContentValidationException>())
                .Which.Errors.Should().Contain(e => e.Contains("chave desconhecida 'ordem'"));
            (await SnapshotAsync()).Should().Equal(before);
        });
    }

    [Fact]
    public async Task SeedAsync_TituloComMaisDe200Caracteres_FalhaAntesDoBanco()
    {
        await WithContentCopy(async copy =>
        {
            var lessonFile = Path.Combine(ModuleDir(copy, "sql-dados", "crud"), "01-crud-completo.md");
            File.WriteAllText(lessonFile, File.ReadAllText(lessonFile)
                .Replace("title: \"CRUD completo\"", $"title: \"{new string('a', 201)}\""));

            var act = () => SeedAsync(copy);

            (await act.Should().ThrowAsync<ContentValidationException>())
                .Which.Errors.Should().ContainSingle(e => e.Contains("01-crud-completo.md") && e.Contains("(máximo 200)"));
            (await CountRowsAsync()).Should().Be((0, 0, 0));
        });
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

    private async Task WithContentCopy(Func<string, Task> test)
    {
        var copy = CopyContentToTemp();
        try
        {
            await test(copy);
        }
        finally
        {
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }
        }
    }

    private static string ModuleDir(string root, string path, string module) =>
        Path.Combine(root, "paths", path, "modules", module);

    // Renomeia NN-slug.md para o novo prefixo e ajusta o "order:" do frontmatter.
    private static void RenumberLesson(string moduleDir, string fileName, int newOrder)
    {
        var source = Path.Combine(moduleDir, fileName);
        var text = Regex.Replace(File.ReadAllText(source), @"(?m)^order: \d+$", $"order: {newOrder}");
        var target = Path.Combine(moduleDir, $"{newOrder:00}-{fileName[3..]}");
        File.Delete(source);
        File.WriteAllText(target, text);
    }

    private static void EditPathYaml(string root, string path, Func<string, string> edit)
    {
        var file = Path.Combine(root, "paths", path, "path.yaml");
        var original = File.ReadAllText(file).ReplaceLineEndings("\n");
        var edited = edit(original);
        edited.Should().NotBe(original, "a edição do path.yaml no teste precisa mudar algo");
        File.WriteAllText(file, edited);
    }

    // Tira o item do módulo da lista e renumera os módulos seguintes.
    private static string RemoveModuleEntry(string yaml, string slug)
    {
        var entries = Regex.Split(yaml, @"(?=\n  - slug: )");
        var removed = entries.Single(e => e.StartsWith($"\n  - slug: {slug}\n"));
        var removedOrder = int.Parse(Regex.Match(removed, @"order: (\d+)").Groups[1].Value);
        return string.Concat(entries.Where(e => e != removed).Select(e =>
            e.StartsWith("\n  - slug: ")
                ? Regex.Replace(e, @"order: (\d+)", m =>
                    int.Parse(m.Groups[1].Value) is var o && o > removedOrder ? $"order: {o - 1}" : m.Value)
                : e));
    }

    private async Task<bool> LessonPublishedAsync(string module, string slug)
    {
        await using var db = CreateDbContext();
        return await db.Lessons.Where(l => l.Slug == slug && db.Modules.Any(m => m.Id == l.ModuleId && m.Slug == module))
            .Select(l => l.IsPublished).SingleAsync();
    }

    // UserProgress.UserId tem FK para AspNetUsers (Identity).
    private async Task<Guid> AddProgressAsync(Guid lessonId)
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"{userId:N}@teste.dev", Email = $"{userId:N}@teste.dev", DisplayName = "Aluno" });
        db.UserProgress.Add(UserProgress.StartLesson(userId, lessonId));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> LessonIdAsync(string module, string slug)
    {
        await using var db = CreateDbContext();
        return await db.Lessons.Where(l => l.Slug == slug && db.Modules.Any(m => m.Id == l.ModuleId && m.Slug == module))
            .Select(l => l.Id).SingleAsync();
    }

    private async Task<List<string>> LessonSlugsInOrderAsync(string module)
    {
        await using var db = CreateDbContext();
        var moduleId = await db.Modules.Where(m => m.Slug == module).Select(m => m.Id).SingleAsync();
        return await db.Lessons.Where(l => l.ModuleId == moduleId).OrderBy(l => l.Order).Select(l => l.Slug).ToListAsync();
    }

    private async Task<Dictionary<string, Guid>> ModuleIdsBySlugAsync(string path)
    {
        await using var db = CreateDbContext();
        return await db.Modules.Where(m => db.CareerPaths.Any(p => p.Id == m.CareerPathId && p.Slug == path)).ToDictionaryAsync(m => m.Slug, m => m.Id);
    }

    private async Task<List<string>> SnapshotAsync()
    {
        await using var db = CreateDbContext();
        var paths = await db.CareerPaths.Select(p => $"P|{p.Id}|{p.Slug}|{p.Title}|{p.Order}|{p.IsActive}").ToListAsync();
        var modules = await db.Modules.Select(m => $"M|{m.Id}|{m.Slug}|{m.Title}|{m.Order}|{m.IsPublished}|{m.EstimatedMinutes}").ToListAsync();
        var lessons = await db.Lessons.Select(l => $"L|{l.Id}|{l.Slug}|{l.Title}|{l.Order}|{l.IsPublished}|{l.Content.Length}").ToListAsync();
        return paths.Concat(modules).Concat(lessons).Order(StringComparer.Ordinal).ToList();
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
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
