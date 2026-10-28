using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Persistence;

/// <summary>
/// O banco é a última linha de defesa: mesmo que alguém pule o domínio (SQL na mão,
/// script de seed), os CHECKs e índices únicos barram dados inválidos.
/// </summary>
public sealed class ConstraintsTests : IAsyncLifetime
{
    // Códigos de erro do PostgreSQL: https://www.postgresql.org/docs/16/errcodes-appendix.html
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ModulosComMesmaOrdemNaMesmaTrilha_SaoRejeitados()
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        db.CareerPaths.Add(path);
        db.Modules.Add(Module.Create(path.Id, "Módulo A", order: 1));
        db.Modules.Add(Module.Create(path.Id, "Módulo B", order: 1));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task AulasComMesmaOrdemNoMesmoModulo_SaoRejeitadas()
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        var module = path.AddModule("Fundamentos");
        db.CareerPaths.Add(path);
        db.Lessons.Add(Lesson.Create(module.Id, "Aula A", ContentType.Markdown, "# A", order: 1));
        db.Lessons.Add(Lesson.Create(module.Id, "Aula B", ContentType.Markdown, "# B", order: 1));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task AulaDespublicada_EhGravadaComoFalse()
    {
        // Protege contra o "sentinela" do EF: com HasDefaultValue(true), false não seria enviado no INSERT.
        Guid lessonId;
        await using (var db = CreateDbContext())
        {
            var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
            var lesson = path.AddModule("Fundamentos").AddLesson("Removida", ContentType.Markdown, "# x");
            lesson.Unpublish();
            lessonId = lesson.Id;
            db.CareerPaths.Add(path);
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        (await check.Lessons.SingleAsync(l => l.Id == lessonId)).IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task AulaInseridaSemIsPublished_FicaPublicadaPeloDefaultDaMigration()
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        var module = path.AddModule("Fundamentos");
        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();

        // Simula uma aula que já existia antes da migration AddLessonIsPublished.
        var lessonId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Lessons" ("Id", "ModuleId", "Title", "Slug", "ContentType", "Content", "Order", "EstimatedMinutes", "Difficulty")
            VALUES ({lessonId}, {module.Id}, 'Antiga', 'antiga', 'Markdown', '# antiga', 1, 0, 'Beginner')
            """);

        (await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == lessonId)).IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task ModulosComMesmoSlugNaMesmaTrilha_SaoRejeitados()
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        db.CareerPaths.Add(path);
        db.Modules.Add(Module.Create(path.Id, "Fundamentos", order: 1));
        db.Modules.Add(Module.Create(path.Id, "Fundamentos!", order: 2)); // slug "fundamentos" de novo

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_Modules_CareerPathId_Slug");
    }

    [Fact]
    public async Task AulasComMesmoSlugNoMesmoModulo_SaoRejeitadas()
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        var module = path.AddModule("Fundamentos");
        db.CareerPaths.Add(path);
        db.Lessons.Add(Lesson.Create(module.Id, "Variáveis", ContentType.Markdown, "# A", order: 1));
        db.Lessons.Add(Lesson.Create(module.Id, "Variaveis", ContentType.Markdown, "# B", order: 2));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_Lessons_ModuleId_Slug");
    }

    [Fact]
    public async Task MesmoSlugEmTrilhasDiferentes_EhPermitido()
    {
        await using var db = CreateDbContext();
        var pathA = CareerPath.Create($"Trilha A {Guid.NewGuid():N}");
        var pathB = CareerPath.Create($"Trilha B {Guid.NewGuid():N}");
        pathA.AddModule("Fundamentos");
        pathB.AddModule("Fundamentos");
        db.CareerPaths.AddRange(pathA, pathB);

        var act = () => db.SaveChangesAsync();

        await act.Should().NotThrowAsync("o slug é único por trilha, não global");
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public async Task ProgressoDaMatriculaForaDe0a100_EhRejeitado(decimal percentage)
    {
        await using var db = CreateDbContext();
        var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
        db.CareerPaths.Add(path);
        await db.SaveChangesAsync();

        // SQL direto, de propósito: o domínio já barraria esse valor antes de chegar aqui.
        var act = () => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UserPathEnrollments" ("UserId", "CareerPathId", "EnrolledAt", "ProgressPercentage", "IsActive")
            VALUES ({Guid.NewGuid()}, {path.Id}, now(), {percentage}, true)
            """);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [Fact]
    public async Task ProgressoSemAlvoOuComTempoNegativo_EhRejeitado()
    {
        await using var db = CreateDbContext();

        var semAlvo = () => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UserProgress" ("Id", "UserId", "Status", "TimeSpentSeconds", "LastAccessedAt")
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, 'InProgress', 0, now())
            """);
        var tempoNegativo = () => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "CodeSubmissions" ("Id", "UserId", "ExerciseId", "SubmittedCode", "Result", "ExecutionTimeMs", "SubmittedAt")
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'x', 'Pass', -5, now())
            """);

        (await semAlvo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
        // O CHECK é avaliado antes da FK de ExerciseId, então o erro é de CHECK mesmo.
        (await tempoNegativo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    private DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
