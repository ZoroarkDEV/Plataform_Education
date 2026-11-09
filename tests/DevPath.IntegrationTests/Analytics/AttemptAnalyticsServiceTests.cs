using System.Text.RegularExpressions;
using DevPath.Application.Analytics;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Analytics;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Persistence;
using DevPath.Infrastructure.Progress;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Analytics;

/// <summary>
/// Banco com tentativas e "Seguir sem pontos" escritos com hora exata (#43):
///   "Analise" (ativa): módulo 1 (publicado): Aula 1 (E1) · Aula 2 (E2 + E2 oculto) · Rascunho (draft, ED) ·
///   Oculta (despublicada, EH) · Dupla (E5a + E5b). Módulo 2 (rascunho): Escondida (EM).
///   "Desativada" (inativa): Velha (EI).
/// Só as aulas 1, 2 e Dupla podem aparecer no relatório.
/// </summary>
public sealed class AttemptAnalyticsFixture : IAsyncLifetime
{
    private const string Cases = """[{"name":"c","expected":{"columns":["x"],"rows":[["1"]]},"orderMatters":false}]""";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public Lesson Lesson1 { get; private set; } = null!;
    public Lesson Lesson2 { get; private set; } = null!;
    public Lesson Draft { get; private set; } = null!;
    public Lesson Hidden { get; private set; } = null!;
    public Lesson Double { get; private set; } = null!;
    public Lesson InDraftModule { get; private set; } = null!;
    public Lesson InInactivePath { get; private set; } = null!;
    public Guid[] Users { get; } = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

    public AttemptsReport Report { get; private set; } = null!;

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var path = CareerPath.Create("Analise", order: 1);
        var m1 = path.AddModule("Modulo 1");
        m1.Publish();
        Lesson1 = m1.AddLesson("Aula 1", ContentType.Markdown, "# 1");
        var e1 = Lesson1.AddExercise("E1", "x", "sql", Cases);
        Lesson2 = m1.AddLesson("Aula 2", ContentType.Markdown, "# 2");
        var e2 = Lesson2.AddExercise("E2", "x", "sql", Cases);
        var e2Hidden = Lesson2.AddExercise("E2 oculto", "x", "sql", Cases);
        e2Hidden.Unpublish();
        Draft = m1.AddLesson("Rascunho", ContentType.Markdown, "# r", isDraft: true);
        var eDraft = Draft.AddExercise("ED", "x", "sql", Cases);
        Hidden = m1.AddLesson("Oculta", ContentType.Markdown, "# o");
        Hidden.Unpublish();
        var eHidden = Hidden.AddExercise("EH", "x", "sql", Cases);
        Double = m1.AddLesson("Dupla", ContentType.Markdown, "# d");
        var e5a = Double.AddExercise("E5a", "x", "sql", Cases);
        var e5b = Double.AddExercise("E5b", "x", "sql", Cases);
        var m2 = path.AddModule("Modulo 2"); // nasce rascunho
        InDraftModule = m2.AddLesson("Escondida", ContentType.Markdown, "# e");
        var eModule = InDraftModule.AddExercise("EM", "x", "sql", Cases);

        var inactive = CareerPath.Create("Desativada", order: 2);
        inactive.Deactivate();
        var mi = inactive.AddModule("Mi");
        mi.Publish();
        InInactivePath = mi.AddLesson("Velha", ContentType.Markdown, "# v");
        var eInactive = InInactivePath.AddExercise("EI", "x", "sql", Cases);

        db.CareerPaths.AddRange(path, inactive);
        foreach (var id in Users)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = id, UserName = $"{id:N}@t", NormalizedUserName = $"{id:N}@T", Email = $"{id:N}@t",
                NormalizedEmail = $"{id:N}@T", DisplayName = "Aluno", SecurityStamp = Guid.NewGuid().ToString()
            });
        }
        await db.SaveChangesAsync();

        var (u1, u2, u3, u4, u5, u6) = (Users[0], Users[1], Users[2], Users[3], Users[4], Users[5]);
        var minute = 0;
        async Task Try(Guid user, Exercise e, string? outcome)
        {
            var result = outcome == "Pass" ? "Pass" : "Fail";
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "CodeSubmissions" ("Id", "UserId", "ExerciseId", "SubmittedCode", "Result", "Outcome", "ExecutionTimeMs", "SubmittedAt")
                VALUES (gen_random_uuid(), @u, @e, 'SELECT 1;', @r, @o, 1, @at)
                """,
                new NpgsqlParameter("u", user), new NpgsqlParameter("e", e.Id), new NpgsqlParameter("r", result),
                new NpgsqlParameter("o", (object?)outcome ?? DBNull.Value), new NpgsqlParameter("at", T0.AddMinutes(minute++)));
        }

        // Aula 1: u1 = 3 até acertar (+2 depois), u2 = 1, u3 nunca acerta (5), u4 = 7, u5 = 2.
        foreach (var o in new[] { "Fail", "Error", "Pass", "Fail", "Pass" }) await Try(u1, e1, o);
        await Try(u2, e1, "Pass");
        foreach (var o in new[] { "Fail", "Fail", "Timeout", "Fail", "Fail" }) await Try(u3, e1, o);
        for (var i = 0; i < 6; i++) await Try(u4, e1, "Fail");
        await Try(u4, e1, "Pass");
        await Try(u5, e1, "Fail");
        await Try(u5, e1, "Pass");

        // Aula 2: u1 = falha antiga sem detalhe (Outcome NULL) + acerto = 2; u2 = 1.
        // O exercício oculto não pode exigir acerto nem contar tentativa.
        await Try(u1, e2, null);
        await Try(u1, e2, "Pass");
        await Try(u2, e2Hidden, "Fail");
        await Try(u2, e2, "Pass");

        // Dupla: u1 acerta E5a e depois E5b na 3ª tentativa da aula (a 4ª, errada, vem depois). u2 só acerta E5a.
        await Try(u1, e5a, "Fail");
        await Try(u1, e5a, "Pass");
        await Try(u1, e5b, "Pass");
        await Try(u1, e5b, "Fail");
        await Try(u2, e5a, "Pass");

        // Aulas que NÃO podem aparecer, com tentativas e Seguir sem pontos.
        foreach (var e in new[] { eDraft, eHidden, eModule, eInactive })
        {
            await Try(u6, e, "Fail");
            await Try(u6, e, "Pass");
        }

        async Task Progress(Guid user, Lesson lesson, string kind, bool skipped) =>
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "Score", "TimeSpentSeconds", "CompletedAt", "LastAccessedAt", "CompletionKind", "SkippedAt")
                VALUES (gen_random_uuid(), @u, @l, 'Completed', 0, 0, @at, @at, @k, @s)
                """,
                new NpgsqlParameter("u", user), new NpgsqlParameter("l", lesson.Id), new NpgsqlParameter("at", T0),
                new NpgsqlParameter("k", kind), new NpgsqlParameter("s", skipped ? T0 : DBNull.Value));

        // Seguir sem pontos: Aula 1 = 2 alunos (u3 continua, u4 acertou depois); Aula 2 = 1. u1 concluiu a Aula 1 sem pular.
        await Progress(u3, Lesson1, "SkippedAfterFailures", skipped: true);
        await Progress(u4, Lesson1, "ExercisePassed", skipped: true);
        await Progress(u1, Lesson1, "ExercisePassed", skipped: false);
        await Progress(u5, Lesson2, "SkippedAfterFailures", skipped: true);
        foreach (var lesson in new[] { Draft, Hidden, InDraftModule, InInactivePath })
        {
            await Progress(u6, lesson, "SkippedAfterFailures", skipped: true);
        }

        Report = await new AttemptAnalyticsService(db).GetReportAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

public sealed class AttemptAnalyticsServiceTests(AttemptAnalyticsFixture fx) : IClassFixture<AttemptAnalyticsFixture>
{
    [Fact]
    public void AteAcertar_Aula1_MedianaMediaEDistribuicao_SoAteOPrimeiroAcerto()
    {
        var row = fx.Report.AttemptsUntilPass.Single(r => r.Lesson.LessonId == fx.Lesson1.Id);

        row.StudentsTried.Should().Be(5);
        row.StudentsPassed.Should().Be(4, "u3 nunca acertou e fica fora da mediana");
        row.MedianAttempts.Should().Be(2.5, "tentativas até acertar: 1, 2, 3 e 7");
        row.AverageAttempts.Should().Be(3.25);
        row.Distribution.Should().Be(new AttemptsDistribution(One: 1, Two: 1, Three: 1, Four: 0, Five: 0, SixToTen: 1, ElevenOrMore: 0));
        row.Lesson.LessonTitle.Should().Be("Aula 1");
        row.Lesson.ModuleOrder.Should().Be(1);
        row.Lesson.LessonOrder.Should().Be(1);
    }

    [Fact]
    public void AteAcertar_ExercicioOcultoNaoConta_EFalhaAntigaConta()
    {
        var row = fx.Report.AttemptsUntilPass.Single(r => r.Lesson.LessonId == fx.Lesson2.Id);

        row.StudentsTried.Should().Be(2);
        row.StudentsPassed.Should().Be(2, "o exercício despublicado não precisa de acerto");
        row.MedianAttempts.Should().Be(1.5);
        row.Distribution.One.Should().Be(1);
        row.Distribution.Two.Should().Be(1);
    }

    [Fact]
    public void AteAcertar_AulaComDoisExercicios_SoContaQuemAcertouTodos()
    {
        var row = fx.Report.AttemptsUntilPass.Single(r => r.Lesson.LessonId == fx.Double.Id);

        row.StudentsTried.Should().Be(2);
        row.StudentsPassed.Should().Be(1, "u2 acertou só um dos dois exercícios");
        row.MedianAttempts.Should().Be(3);
        row.Distribution.Three.Should().Be(1);
    }

    [Fact]
    public void Relatorio_SoTemAulasVisiveis_NaOrdemDaTrilha()
    {
        var hidden = new[] { fx.Draft.Id, fx.Hidden.Id, fx.InDraftModule.Id, fx.InInactivePath.Id };

        fx.Report.AttemptsUntilPass.Select(r => r.Lesson.LessonId)
            .Should().Equal(fx.Lesson1.Id, fx.Lesson2.Id, fx.Double.Id);
        fx.Report.Skips.Select(r => r.Lesson.LessonId).Should().NotContain(hidden);
        fx.Report.FailureRates.Select(r => r.Lesson.LessonId).Should().NotContain(hidden);
    }

    [Fact]
    public void SeguirSemPontos_ContaQuemUsou_InclusiveQuemAcertouDepois_MaisUsadaPrimeiro()
    {
        fx.Report.Skips.Should().HaveCount(2);

        var first = fx.Report.Skips[0];
        first.Lesson.LessonId.Should().Be(fx.Lesson1.Id);
        first.StudentsSkipped.Should().Be(2);
        first.StillSkipped.Should().Be(1);
        first.PassedAfterSkip.Should().Be(1);

        var second = fx.Report.Skips[1];
        second.Lesson.LessonId.Should().Be(fx.Lesson2.Id);
        second.Should().BeEquivalentTo(new { StudentsSkipped = 1, StillSkipped = 1, PassedAfterSkip = 0 });
    }

    [Fact]
    public void TaxaDeFalha_PorAula_SeparaErradoErroESemDetalhe()
    {
        var l1 = fx.Report.FailureRates.Single(r => r.Lesson.LessonId == fx.Lesson1.Id);
        l1.Should().BeEquivalentTo(new
        {
            Attempts = 20, Students = 5, Passed = 5, Wrong = 13, Errors = 2, FailedWithoutDetail = 0, FailureRatePercent = 75.0m
        });

        var l2 = fx.Report.FailureRates.Single(r => r.Lesson.LessonId == fx.Lesson2.Id);
        l2.Should().BeEquivalentTo(new
        {
            Attempts = 3, Students = 2, Passed = 2, Wrong = 0, Errors = 0, FailedWithoutDetail = 1, FailureRatePercent = 33.3m
        });

        fx.Report.FailureRates[0].Lesson.LessonId.Should().Be(fx.Lesson1.Id, "a maior taxa de falha vem primeiro");
    }

    [Fact]
    public async Task SeguirSemPontos_GravadoPeloWriter_SobreviveAoAcertoDepois()
    {
        await using var db = fx.CreateDbContext();
        var user = fx.Users[5];
        var writer = new LessonProgressWriter(db, new JourneySnapshotLoader(db));

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await writer.MarkCompletedAsync(user, fx.Lesson2.Id, LessonCompletionKind.SkippedAfterFailures, 0);
            await tx.CommitAsync();
        }
        var skippedAt = (await db.UserProgress.AsNoTracking().SingleAsync(p => p.UserId == user && p.LessonId == fx.Lesson2.Id)).SkippedAt;
        skippedAt.Should().NotBeNull();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await writer.MarkCompletedAsync(user, fx.Lesson2.Id, LessonCompletionKind.ExercisePassed, 10);
            await tx.CommitAsync();
        }
        var row = await db.UserProgress.AsNoTracking().SingleAsync(p => p.UserId == user && p.LessonId == fx.Lesson2.Id);
        row.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        row.SkippedAt.Should().Be(skippedAt);

        // Uma conclusão comum (sem pular) não grava SkippedAt.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await writer.MarkCompletedAsync(user, fx.Lesson1.Id, LessonCompletionKind.ExercisePassed, 10);
            await tx.CommitAsync();
        }
        (await db.UserProgress.AsNoTracking().SingleAsync(p => p.UserId == user && p.LessonId == fx.Lesson1.Id))
            .SkippedAt.Should().BeNull();
    }

    [Fact]
    public async Task Banco_RecusaOutcomeQueContradizResult()
    {
        await using var db = fx.CreateDbContext();
        var exerciseId = await db.Exercises.Where(e => e.LessonId == fx.Lesson1.Id).Select(e => e.Id).FirstAsync();

        var act = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "CodeSubmissions" ("Id", "UserId", "ExerciseId", "SubmittedCode", "Result", "Outcome", "ExecutionTimeMs", "SubmittedAt")
            VALUES (gen_random_uuid(), @u, @e, 'x', 'Fail', 'Pass', 1, now())
            """, new NpgsqlParameter("u", fx.Users[0]), new NpgsqlParameter("e", exerciseId));

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
            .Should().Be("CK_CodeSubmissions_Outcome_MatchesResult");
    }

    [Fact]
    public void Documentacao_TemAsMesmasConsultasDoCodigo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevPath.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("os testes rodam dentro do repositório");
        var doc = Normalize(File.ReadAllText(Path.Combine(dir!.FullName, "docs", "analytics", "tentativas.md")));

        foreach (var sql in new[] { AttemptAnalyticsSql.AttemptsUntilPass, AttemptAnalyticsSql.Skips, AttemptAnalyticsSql.FailureRates })
        {
            doc.Should().Contain(Normalize(sql), "docs/analytics/tentativas.md precisa ter a consulta igual à da página");
        }

        static string Normalize(string s) => Regex.Replace(s, @"\s+", " ").Trim();
    }
}
