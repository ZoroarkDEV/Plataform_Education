using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Persistence;

/// <summary>
/// Sobe um PostgreSQL 16 descartável (Testcontainers) e aplica as migrations do zero.
/// Precisa do Docker rodando na máquina.
/// </summary>
public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build(); // mesma imagem do docker-compose

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrateAsync_EmBancoLimpo_CriaTodasAsTabelasSemPendencias()
    {
        await using var db = CreateDbContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_InitialCreate"))
            .And.Contain(m => m.EndsWith("_AddIdentity"));

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        tables.Should().Contain(
        [
            "CareerPaths", "Modules", "Lessons", "Exercises",
            "UserProgress", "UserPathEnrollments",
            "Achievements", "UserAchievements", "CodeSubmissions",
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles"
        ]);
    }

    [Fact]
    public async Task Slug_Duplicado_ViolaIndiceUnico()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.CareerPaths.Add(Domain.CareerPaths.CareerPath.Create("Backend .NET"));
        db.CareerPaths.Add(Domain.CareerPaths.CareerPath.Create("Backend .NET"));
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AddLessonProgressTracking_DeduplicaMantendoAMaisAvancada_ECriaOIndiceUnico()
    {
        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029125122_AddExerciseCommonErrorsAndIsPublished"); // a migration anterior

        var path = Domain.CareerPaths.CareerPath.Create("Trilha dedupe");
        var module = path.AddModule("M");
        var a = module.AddLesson("A", Domain.CareerPaths.ContentType.Markdown, "# a");
        var b = module.AddLesson("B", Domain.CareerPaths.ContentType.Markdown, "# b");
        db.CareerPaths.Add(path);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = $"{userId:N}@t", NormalizedUserName = $"{userId:N}@T", Email = $"{userId:N}@t",
            NormalizedEmail = $"{userId:N}@T", DisplayName = "A", SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        Guid keepA = Guid.NewGuid(), keepB = Guid.NewGuid();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        async Task InsertAsync(Guid id, Guid lessonId, string status, DateTime lastAccess) =>
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "TimeSpentSeconds", "LastAccessedAt", "CompletedAt")
                VALUES (@id, @user, @lesson, @status, 0, @last, CASE WHEN @status IN ('Completed','Mastered') THEN @last END)
                """,
                new NpgsqlParameter("id", id), new NpgsqlParameter("user", userId), new NpgsqlParameter("lesson", lessonId),
                new NpgsqlParameter("status", status), new NpgsqlParameter("last", lastAccess));

        // A: Completed antigo vence InProgress recente. B: entre dois InProgress, vence o aberto por último.
        await InsertAsync(Guid.NewGuid(), a.Id, "InProgress", t0.AddDays(5));
        await InsertAsync(keepA, a.Id, "Completed", t0);
        await InsertAsync(Guid.NewGuid(), a.Id, "InProgress", t0.AddDays(1));
        await InsertAsync(Guid.NewGuid(), b.Id, "InProgress", t0);
        await InsertAsync(keepB, b.Id, "InProgress", t0.AddDays(2));

        await db.Database.MigrateAsync();

        var ids = await db.Database.SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM "UserProgress" """).ToListAsync();
        ids.Should().BeEquivalentTo([keepA, keepB]);

        db.UserProgress.Add(UserProgress.StartLesson(userId, a.Id));
        var duplicate = () => db.SaveChangesAsync();
        (await duplicate.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("IX_UserProgress_UserId_LessonId");
    }

    [Fact]
    public async Task CompletionKind_SoEmAulaConcluida()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var path = Domain.CareerPaths.CareerPath.Create("Trilha ck");
        var lesson = path.AddModule("M").AddLesson("A", Domain.CareerPaths.ContentType.Markdown, "# a");
        db.CareerPaths.Add(path);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = $"{userId:N}@t", NormalizedUserName = $"{userId:N}@T", Email = $"{userId:N}@t",
            NormalizedEmail = $"{userId:N}@T", DisplayName = "A", SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "TimeSpentSeconds", "LastAccessedAt", "CompletionKind")
            VALUES (gen_random_uuid(), @user, @lesson, 'InProgress', 0, now(), 'Read')
            """, new NpgsqlParameter("user", userId), new NpgsqlParameter("lesson", lesson.Id));

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("CK_UserProgress_CompletionKind");
    }



    [Fact]
    public async Task AddLessonProgressTracking_Down_RemoveColunasERestauraIndiceNaoUnico()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261029125122_AddExerciseCommonErrorsAndIsPublished");

        (await db.Database.GetAppliedMigrationsAsync())
            .Should().NotContain(m => m.EndsWith("_AddLessonProgressTracking"));

        var completionKindCols = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'UserProgress' AND column_name = 'CompletionKind'
            """).ToListAsync();
        completionKindCols.Should().BeEmpty("Down remove CompletionKind");

        var path = Domain.CareerPaths.CareerPath.Create("Trilha down");
        var lesson = path.AddModule("M").AddLesson("A", Domain.CareerPaths.ContentType.Markdown, "# a");
        db.CareerPaths.Add(path);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = $"{userId:N}@t", NormalizedUserName = $"{userId:N}@T", Email = $"{userId:N}@t",
            NormalizedEmail = $"{userId:N}@T", DisplayName = "A", SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        // Índice não-único: duas linhas (aluno, aula) passam.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "TimeSpentSeconds", "LastAccessedAt")
            VALUES (gen_random_uuid(), @user, @lesson, 'InProgress', 0, now()),
                   (gen_random_uuid(), @user, @lesson, 'InProgress', 0, now())
            """, new NpgsqlParameter("user", userId), new NpgsqlParameter("lesson", lesson.Id));

        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM "UserProgress"
            WHERE "UserId" = @user AND "LessonId" = @lesson
            """, new NpgsqlParameter("user", userId), new NpgsqlParameter("lesson", lesson.Id)).SingleAsync();
        count.Should().Be(2, "Down restaurou o índice não-único");
    }

    [Fact]
    public void Modelo_NaoTemMudancasPendentes_AlemDasMigrations()
    {
        using var db = CreateDbContext();
        var differ = db.GetService<IMigrationsModelDiffer>();
        var assembly = db.GetService<IMigrationsAssembly>();
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var designTime = db.GetService<IDesignTimeModel>().Model;

        var snapshot = assembly.ModelSnapshot
            ?? throw new InvalidOperationException("DevPathDbContextModelSnapshot ausente.");
        var snapshotModel = initializer.Initialize(snapshot.Model, designTime: true, validationLogger: null);

        differ.HasDifferences(
            snapshotModel.GetRelationalModel(),
            designTime.GetRelationalModel()).Should().BeFalse(
            "há mudanças no modelo sem migration: rode 'dotnet ef migrations add …'");
    }

    private DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
