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
        var c = module.AddLesson("C", Domain.CareerPaths.ContentType.Markdown, "# c");
        var d = module.AddLesson("D", Domain.CareerPaths.ContentType.Markdown, "# d");
        db.CareerPaths.Add(path);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = $"{userId:N}@t", NormalizedUserName = $"{userId:N}@T", Email = $"{userId:N}@t",
            NormalizedEmail = $"{userId:N}@T", DisplayName = "A", SecurityStamp = Guid.NewGuid().ToString()
        });
        await SaveOnOldSchemaAsync(db);

        Guid keepA = Guid.NewGuid(), keepB = Guid.NewGuid(), keepC = Guid.NewGuid(), keepD = Guid.NewGuid();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        async Task InsertAsync(
            Guid id, Guid lessonId, string status, DateTime lastAccess, int? score = null, DateTime? completedAt = null) =>
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "UserProgress"
                    ("Id", "UserId", "LessonId", "Status", "Score", "TimeSpentSeconds", "LastAccessedAt", "CompletedAt")
                VALUES (@id, @user, @lesson, @status, @score, 0, @last, @completed)
                """,
                new NpgsqlParameter("id", id), new NpgsqlParameter("user", userId), new NpgsqlParameter("lesson", lessonId),
                new NpgsqlParameter("status", status),
                new NpgsqlParameter("score", (object?)score ?? DBNull.Value),
                new NpgsqlParameter("last", lastAccess),
                new NpgsqlParameter("completed", (object?)completedAt ?? DBNull.Value));

        // A (caso simples): Completed antigo vence InProgress recente.
        await InsertAsync(Guid.NewGuid(), a.Id, "InProgress", t0.AddDays(5));
        await InsertAsync(keepA, a.Id, "Completed", t0, score: null, completedAt: t0);
        await InsertAsync(Guid.NewGuid(), a.Id, "InProgress", t0.AddDays(1));

        // B (caso simples): entre dois InProgress, vence o aberto por último.
        await InsertAsync(Guid.NewGuid(), b.Id, "InProgress", t0);
        await InsertAsync(keepB, b.Id, "InProgress", t0.AddDays(2));

        // C (QA): Completed Score 10 antigo + Completed Score null mais novo → preserva os 10.
        await InsertAsync(keepC, c.Id, "Completed", t0, score: 10, completedAt: t0);
        await InsertAsync(Guid.NewGuid(), c.Id, "Completed", t0.AddDays(3), score: null, completedAt: t0.AddDays(3));

        // D (QA): Completed Score 10 + Mastered Score null → Mastered com Score 10 e CompletedAt do Completed.
        await InsertAsync(Guid.NewGuid(), d.Id, "Completed", t0, score: 10, completedAt: t0);
        await InsertAsync(keepD, d.Id, "Mastered", t0.AddDays(4), score: null, completedAt: null);

        await db.Database.MigrateAsync();

        var rows = await db.Database.SqlQueryRaw<DedupeRow>("""
            SELECT "Id", "LessonId", "Status", "Score", "CompletedAt", "LastAccessedAt"
            FROM "UserProgress"
            ORDER BY "LessonId"
            """).ToListAsync();

        rows.Should().HaveCount(4);
        rows.Select(r => r.Id).Should().BeEquivalentTo([keepA, keepB, keepC, keepD]);

        var rowC = rows.Single(r => r.Id == keepC);
        rowC.Status.Should().Be("Completed");
        rowC.Score.Should().Be(10, "Completed(10) antigo não perde pontos para Completed(null) mais novo");
        rowC.CompletedAt.Should().Be(t0, "fica a conclusão mais antiga");
        rowC.LastAccessedAt.Should().Be(t0.AddDays(3), "fica o LastAccessedAt mais recente");

        var rowD = rows.Single(r => r.Id == keepD);
        rowD.Status.Should().Be("Mastered");
        rowD.Score.Should().Be(10, "Mastered herda o MAX(Score) do Completed");
        rowD.CompletedAt.Should().Be(t0, "Mastered herda o MIN(CompletedAt) do Completed");
        rowD.LastAccessedAt.Should().Be(t0.AddDays(4));

        db.UserProgress.Add(UserProgress.StartLesson(userId, a.Id));
        var duplicate = () => db.SaveChangesAsync();
        (await duplicate.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("IX_UserProgress_UserId_LessonId");
    }

    /// <summary>
    /// Salva o grafo com o modelo atual num banco parado numa migration antiga. O modelo já tem colunas de
    /// migrations posteriores (Lessons.CompletionSummary #41, Modules.CompletionSubtitle #42), que o INSERT
    /// do EF manda mesmo nulas. As colunas entram só durante o SaveChanges e saem logo depois, para o banco
    /// continuar no esquema da migration antiga.
    /// </summary>
    private static async Task SaveOnOldSchemaAsync(DevPathDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Lessons" ADD COLUMN "CompletionSummary" character varying(200)""");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Modules" ADD COLUMN "CompletionSubtitle" character varying(200)""");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Lessons" DROP COLUMN "CompletionSummary" """);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Modules" DROP COLUMN "CompletionSubtitle" """);
    }

    private sealed class DedupeRow
    {
        public Guid Id { get; set; }
        public Guid LessonId { get; set; }
        public string Status { get; set; } = "";
        public int? Score { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
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
        await SaveOnOldSchemaAsync(db);

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
    public async Task AddAttemptOutcomeAndSkippedAt_PreencheOQueDa_EDownRemove()
    {
        const string Previous = "20261105115835_AddLessonProgressTracking";
        await using var db = CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        // Seed em SQL puro, só com as colunas que existiam NESTE passo: o modelo do EF muda com as migrations futuras
        // (ex.: #41 adiciona colunas) e um SaveChanges aqui tentaria gravar colunas que ainda não existem.
        Guid userId = Guid.NewGuid(), pathId = Guid.NewGuid(), moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid(), lesson2Id = Guid.NewGuid(), exerciseId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
                "SecurityStamp", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                "DisplayName", "CreatedAt", "IsAdmin")
            VALUES (@u, 'a@t', 'A@T', 'a@t', 'A@T', false, 'x', false, false, false, 0, 'A', now(), false);
            INSERT INTO "CareerPaths" ("Id", "Title", "Slug", "Description", "DifficultyLevel", "EstimatedHours", "IsActive", "Order")
            VALUES (@p, 'Trilha tentativas', 'trilha-tentativas', '', 'Beginner', 0, true, 0);
            INSERT INTO "Modules" ("Id", "CareerPathId", "Title", "Slug", "Description", "Order", "EstimatedMinutes", "IsPublished")
            VALUES (@m, @p, 'M', 'm', '', 1, 0, true);
            INSERT INTO "Lessons" ("Id", "ModuleId", "Title", "Slug", "ContentType", "Content", "Order", "EstimatedMinutes",
                "Difficulty", "IsPublished", "IsDraft")
            VALUES (@l, @m, 'A', 'a', 'Markdown', '# a', 1, 0, 'Beginner', true, false),
                   (@l2, @m, 'B', 'b', 'Markdown', '# b', 2, 0, 'Beginner', true, false);
            INSERT INTO "Exercises" ("Id", "LessonId", "Title", "Statement", "StarterCode", "SolutionCode", "TestCases",
                "Language", "Points", "IsPublished")
            VALUES (@e, @l, 'E', 'x', '', '', '[]', 'sql', 10, true);
            """,
            new NpgsqlParameter("u", userId), new NpgsqlParameter("p", pathId), new NpgsqlParameter("m", moduleId),
            new NpgsqlParameter("l", lessonId), new NpgsqlParameter("l2", lesson2Id), new NpgsqlParameter("e", exerciseId));

        var completedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var p = new[] { new NpgsqlParameter("u", userId), new NpgsqlParameter("e", exerciseId), new NpgsqlParameter("l", lessonId),
            new NpgsqlParameter("l2", lesson2Id), new NpgsqlParameter("at", completedAt) };
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "CodeSubmissions" ("Id", "UserId", "ExerciseId", "SubmittedCode", "Result", "ExecutionTimeMs", "SubmittedAt")
            VALUES (gen_random_uuid(), @u, @e, 'a', 'Fail', 1, now()), (gen_random_uuid(), @u, @e, 'b', 'Pass', 1, now());
            INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "Score", "TimeSpentSeconds", "CompletedAt", "LastAccessedAt", "CompletionKind")
            VALUES (gen_random_uuid(), @u, @l, 'Completed', 0, 0, @at, @at, 'SkippedAfterFailures'),
                   (gen_random_uuid(), @u, @l2, 'Completed', 10, 0, @at, @at, 'ExercisePassed');
            """, p);

        await db.Database.MigrateAsync();

        var outcomes = await db.Database.SqlQueryRaw<string>("""
            SELECT "Result" || ':' || COALESCE("Outcome", 'null') AS "Value" FROM "CodeSubmissions" WHERE "UserId" = @u
            """, new NpgsqlParameter("u", userId)).ToListAsync();
        outcomes.Should().BeEquivalentTo(["Pass:Pass", "Fail:null"], "falha antiga não diz se foi errado ou erro");

        var skipped = await db.Database.SqlQueryRaw<DateTime?>("""
            SELECT "SkippedAt" AS "Value" FROM "UserProgress" WHERE "UserId" = @u ORDER BY "LessonId" = @l DESC
            """, new NpgsqlParameter("u", userId), new NpgsqlParameter("l", lessonId)).ToListAsync();
        skipped.Should().Equal(completedAt, null); // aula A (skip atual) e depois aula B (ExercisePassed)

        await migrator.MigrateAsync(Previous);

        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name IN ('Outcome', 'SkippedAt')
            """).ToListAsync();
        columns.Should().BeEmpty("Down remove as duas colunas");
        (await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "CodeSubmissions" """).SingleAsync())
            .Should().Be(2, "Down não apaga tentativas");

        await db.Database.MigrateAsync(); // e sobe de novo
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AddModuleCompletionSubtitle_Down_RemoveColuna()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261109131203_AddLessonCompletionSummary");

        (await db.Database.GetAppliedMigrationsAsync())
            .Should().NotContain(m => m.EndsWith("_AddModuleCompletionSubtitle"));

        var cols = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Modules' AND column_name = 'CompletionSubtitle'
            """).ToListAsync();
        cols.Should().BeEmpty("Down remove CompletionSubtitle");
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
