using DevPath.Application.Achievements;
using DevPath.Application.CodeExecution;
using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Domain.Achievements;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Achievements;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Persistence;
using DevPath.Infrastructure.Progress;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Achievements;

/// <summary>
/// Trilha "Selos SQL" (ativa): módulo "Primeiros passos": Seu primeiro SELECT (ex1, 10 pts) · Segunda aula (ex2, 7 pts).
/// Trilha "Selos limite": módulo "Limite": Limite (exLimit, MaxAttempts = 2). Cada teste cria o próprio aluno.
/// </summary>
public sealed class AchievementFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public const string Cases = """[{"name":"um","columns":["x"],"rows":[["a"]],"orderMatters":false}]""";

    public CareerPath Path { get; private set; } = null!;
    public CareerPath LimitPath { get; private set; } = null!;
    public Module Basics { get; private set; } = null!;
    public Module LimitModule { get; private set; } = null!;
    public Lesson Lesson1 { get; private set; } = null!;
    public Lesson Lesson2 { get; private set; } = null!;
    public Lesson LimitLesson { get; private set; } = null!;
    public Exercise Ex1 { get; private set; } = null!;
    public Exercise Ex2 { get; private set; } = null!;
    public Exercise ExLimit { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        Path = CareerPath.Create("Selos SQL", order: 1);
        Basics = Path.AddModule("Primeiros passos");
        Basics.Publish();
        Lesson1 = Basics.AddLesson("Seu primeiro SELECT", ContentType.Markdown, "# 1");
        Ex1 = Lesson1.AddExercise("E1", "x", "sql", Cases, points: 10);
        Lesson2 = Basics.AddLesson("Segunda aula", ContentType.Markdown, "# 2");
        Ex2 = Lesson2.AddExercise("E2", "x", "sql", Cases, points: 7);

        LimitPath = CareerPath.Create("Selos limite", order: 2);
        LimitModule = LimitPath.AddModule("Limite");
        LimitModule.Publish();
        LimitLesson = LimitModule.AddLesson("Limite", ContentType.Markdown, "# l");
        ExLimit = LimitLesson.AddExercise("EL", "x", "sql", Cases, points: 5, maxAttempts: 2);

        db.CareerPaths.AddRange(Path, LimitPath);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task<Guid> UserAsync(CareerPath? enrollIn = null)
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid();
        var email = $"selo-{id:N}@devpath.test";
        db.Users.Add(new ApplicationUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = "Aluno",
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();
        (await new StudentJourneyService(db, new JourneySnapshotLoader(db)).EnrollAsync(id, (enrollIn ?? Path).Slug))
            .Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return id;
    }
}

public sealed class PrimeiroSelectAchievementTests(AchievementFixture fx) : IClassFixture<AchievementFixture>
{
    private const string PassStdout = "x\n[{\"x\":\"a\"}]\n";
    private const string FailStdout = "x\n[{\"x\":\"b\"}]\n";

    private async Task<ExecutionOutcome> SubmitAsync(Guid userId, Exercise exercise, FakeRunner runner)
    {
        await using var db = fx.CreateDbContext();
        return await new ExerciseSubmissionService(db, new DatasetRepository(db), [runner]).SubmitAsync(userId, exercise.Id, "SELECT x;");
    }

    private Task<ExecutionOutcome> PassAsync(Guid userId, Exercise exercise, TimeSpan delay = default) =>
        SubmitAsync(userId, exercise, new FakeRunner(PassStdout, delay: delay));

    private Task<ExecutionOutcome> FailAsync(Guid userId, Exercise exercise) =>
        SubmitAsync(userId, exercise, new FakeRunner(FailStdout));

    private async Task<IReadOnlyList<EarnedAchievementDto>> ListAsync(Guid userId)
    {
        await using var db = fx.CreateDbContext();
        return await new StudentAchievementService(db).ListAsync(userId);
    }

    private async Task<List<UserAchievement>> RowsAsync(Guid userId)
    {
        await using var db = fx.CreateDbContext();
        return await db.UserAchievements.AsNoTracking().Where(u => u.UserId == userId).ToListAsync();
    }

    private async Task<T> ProgressServiceAsync<T>(Func<LessonProgressService, Task<T>> action)
    {
        await using var db = fx.CreateDbContext();
        var loader = new JourneySnapshotLoader(db);
        return await action(new LessonProgressService(db, loader, new LessonProgressWriter(db, loader)));
    }

    /// <summary>Grava direto uma linha de progresso "de antes do #45".</summary>
    private async Task SeedProgressAsync(Guid userId, Lesson lesson, LessonCompletionKind kind, int? score, DateTime completedAt)
    {
        await using var db = fx.CreateDbContext();
        var row = UserProgress.StartLesson(userId, lesson.Id);
        row.Complete(score, kind);
        db.UserProgress.Add(row);
        await db.SaveChangesAsync();
        await db.UserProgress.Where(p => p.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.CompletedAt, completedAt).SetProperty(p => p.LastAccessedAt, completedAt)
            .SetProperty(p => p.SkippedAt, p => p.SkippedAt == null ? null : completedAt));
    }

    private static void ShouldBePrimeiroSelect(EarnedAchievementDto? dto)
    {
        dto.Should().NotBeNull();
        dto!.Title.Should().Be("Primeiro SELECT");
        dto.Description.Should().Be("Você fez um banco de dados responder à sua primeira pergunta.");
        dto.EarnedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Seed_TemOIdFixo_TextoDoPO_ESlugNoCriteria()
    {
        fx.Lesson1.Slug.Should().Be(AchievementIds.PrimeiroSelectBackfillLessonSlug, "o backfill procura esta aula");
        await using var db = fx.CreateDbContext();
        var seeded = await db.Achievements.AsNoTracking().SingleAsync(a => a.Id == AchievementIds.PrimeiroSelect);

        seeded.Title.Should().Be(AchievementIds.PrimeiroSelectTitle);
        seeded.Description.Should().Be(AchievementIds.PrimeiroSelectDescription);
        (await db.Database.SqlQuery<string>($"""
            SELECT "Criteria"->>'slug' AS "Value" FROM "Achievements" WHERE "Id" = {AchievementIds.PrimeiroSelect}
            """).SingleAsync()).Should().Be("primeiro-select");
    }

    [Fact]
    public async Task PrimeiroAcerto_ConcedeEAvisaUmaVez_ComTituloDescricaoEData()
    {
        var user = await fx.UserAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var pass = await PassAsync(user, fx.Ex1);

        pass.Status.Should().Be(ExecutionStatus.Pass);
        pass.LessonCompleted.Should().BeTrue();
        ShouldBePrimeiroSelect(pass.NewlyUnlockedAchievement);
        pass.NewlyUnlockedAchievement!.EarnedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(5));

        var row = (await RowsAsync(user)).Should().ContainSingle().Which;
        row.AchievementId.Should().Be(AchievementIds.PrimeiroSelect);
        new DateTimeOffset(DateTime.SpecifyKind(row.EarnedAt, DateTimeKind.Utc)).Should().Be(pass.NewlyUnlockedAchievement.EarnedAt);

        var list = await ListAsync(user);
        list.Should().ContainSingle().Which.Should().Be(pass.NewlyUnlockedAchievement, "o dashboard mostra a mesma data");
    }

    [Fact]
    public async Task RefazerOuAcertarOutroExercicio_NaoAvisaDeNovo_NemDuplica()
    {
        var user = await fx.UserAsync();
        (await PassAsync(user, fx.Ex1)).NewlyUnlockedAchievement.Should().NotBeNull();

        var redo = await PassAsync(user, fx.Ex1);
        var other = await PassAsync(user, fx.Ex2);

        redo.Status.Should().Be(ExecutionStatus.Pass);
        redo.NewlyUnlockedAchievement.Should().BeNull("refazer a aula não mostra o aviso de novo");
        other.Status.Should().Be(ExecutionStatus.Pass);
        other.NewlyUnlockedAchievement.Should().BeNull("acertar outro exercício não mostra o aviso de novo");
        (await RowsAsync(user)).Should().ContainSingle();
        (await ListAsync(user)).Should().ContainSingle();
    }

    [Fact]
    public async Task PrimeiroAcertoDeQualquerExercicio_Concede_NaoSoAAula1()
    {
        var user = await fx.UserAsync();

        var first = await PassAsync(user, fx.Ex2); // a aula 2 primeiro (bloqueada ou não, o acerto é de verdade)
        var lesson1 = await PassAsync(user, fx.Ex1);

        ShouldBePrimeiroSelect(first.NewlyUnlockedAchievement);
        lesson1.NewlyUnlockedAchievement.Should().BeNull();
        (await RowsAsync(user)).Should().ContainSingle();
    }

    [Fact]
    public async Task FalhaErroETimeout_NaoConcedem_EDepoisOPrimeiroAcertoConcede()
    {
        var user = await fx.UserAsync();

        var fail = await FailAsync(user, fx.Ex1);
        var error = await SubmitAsync(user, fx.Ex1, new FakeRunner("", exitCode: 3,
            stderr: "psql:/tmp/consulta.psql:1: ERROR:  42P01: relation \"livro\" does not exist"));
        var timeout = await SubmitAsync(user, fx.Ex1, new FakeRunner("", timedOut: true));

        fail.Status.Should().Be(ExecutionStatus.Fail);
        error.Status.Should().Be(ExecutionStatus.Error);
        timeout.Status.Should().Be(ExecutionStatus.Timeout);
        new[] { fail, error, timeout }.Should().OnlyContain(o => o.NewlyUnlockedAchievement == null);
        (await RowsAsync(user)).Should().BeEmpty();
        (await ListAsync(user)).Should().BeEmpty("falha, erro e timeout não são acerto, nem no backfill");

        // Mutação: se Fail/Error/Timeout contassem como acerto anterior, este aviso não apareceria.
        ShouldBePrimeiroSelect((await PassAsync(user, fx.Ex1)).NewlyUnlockedAchievement);
    }

    [Fact]
    public async Task SeguirSemPontos_NaoConcede_EOAcertoDepoisConcede()
    {
        var user = await fx.UserAsync();
        for (var i = 0; i < LessonCompletionRule.DefaultFailuresBeforeSkip; i++)
        {
            (await FailAsync(user, fx.Ex1)).NewlyUnlockedAchievement.Should().BeNull();
        }

        var skip = await ProgressServiceAsync(s => s.SkipAsync(user, fx.Path.Slug, fx.Basics.Slug, fx.Lesson1.Slug));

        skip.Kind.Should().Be(LessonCompletionResultKind.Completed);
        skip.State!.CompletionKind.Should().Be(LessonCompletionKind.SkippedAfterFailures);
        (await RowsAsync(user)).Should().BeEmpty("\"Seguir sem pontos\" não dá selo");
        (await ListAsync(user)).Should().BeEmpty("o pulo da aula 1 não conta no backfill");

        var pass = await PassAsync(user, fx.Ex1);
        pass.LessonCompleted.Should().BeTrue("promove o pulo");
        ShouldBePrimeiroSelect(pass.NewlyUnlockedAchievement);
    }

    [Fact]
    public async Task TentativasEsgotadas_NaoConcedem_NemAoFecharAAula()
    {
        var user = await fx.UserAsync(fx.LimitPath);
        await FailAsync(user, fx.ExLimit);
        await FailAsync(user, fx.ExLimit);

        var exhausted = await PassAsync(user, fx.ExLimit);
        var close = await ProgressServiceAsync(s => s.CompleteAsync(user, fx.LimitPath.Slug, fx.LimitModule.Slug, fx.LimitLesson.Slug));

        exhausted.Status.Should().Be(ExecutionStatus.AttemptsExhausted);
        exhausted.NewlyUnlockedAchievement.Should().BeNull();
        close.Kind.Should().Be(LessonCompletionResultKind.Completed);
        close.State!.CompletionKind.Should().Be(LessonCompletionKind.AttemptsExhausted);
        (await RowsAsync(user)).Should().BeEmpty();
        (await ListAsync(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task Backfill_QuemJaConcluiuAAula1ComAcerto_GanhaEmSilencio_ComADataDoAcerto()
    {
        var user = await fx.UserAsync();
        var oldPass = new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc);
        await SeedProgressAsync(user, fx.Lesson1, LessonCompletionKind.ExercisePassed, 10, oldPass);

        var list = await ListAsync(user);
        var again = await ListAsync(user);
        var laterPass = await PassAsync(user, fx.Ex2);

        ShouldBePrimeiroSelect(list.Should().ContainSingle().Which);
        list[0].EarnedAt.Should().Be(new DateTimeOffset(oldPass));
        again.Should().Equal(list, "o backfill é idempotente");
        laterPass.Status.Should().Be(ExecutionStatus.Pass);
        laterPass.NewlyUnlockedAchievement.Should().BeNull("o backfill nunca vira \"desbloqueado\"");
        (await RowsAsync(user)).Should().ContainSingle();
    }

    [Fact]
    public async Task QuemJaTinhaDireito_EAcertaAntesDeAbrirODashboard_GanhaEmSilencio()
    {
        var user = await fx.UserAsync();
        var oldPass = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        await SeedProgressAsync(user, fx.Lesson1, LessonCompletionKind.ExercisePassed, 10, oldPass);

        var pass = await PassAsync(user, fx.Ex2);

        pass.NewlyUnlockedAchievement.Should().BeNull("já tinha direito antes deste acerto");
        var row = (await RowsAsync(user)).Should().ContainSingle().Which;
        row.EarnedAt.Should().Be(oldPass);
    }

    [Fact]
    public async Task Backfill_PorTentativaPassAntiga_DeQualquerExercicio()
    {
        var user = await fx.UserAsync();
        await using (var db = fx.CreateDbContext())
        {
            db.CodeSubmissions.Add(CodeSubmission.Create(user, fx.Ex2.Id, "SELECT 1;", SubmissionResult.Pass, 1));
            await db.SaveChangesAsync();
        }

        (await ListAsync(user)).Should().ContainSingle();
        (await PassAsync(user, fx.Ex1)).NewlyUnlockedAchievement.Should().BeNull();
    }

    [Theory]
    [InlineData(LessonCompletionKind.SkippedAfterFailures, null)]
    [InlineData(LessonCompletionKind.AttemptsExhausted, 0)]
    [InlineData(LessonCompletionKind.Read, null)]
    public async Task Backfill_AulaUmSemAcerto_NaoConcede(LessonCompletionKind kind, int? score)
    {
        var user = await fx.UserAsync();
        await SeedProgressAsync(user, fx.Lesson1, kind, score, DateTime.UtcNow.AddDays(-3));

        (await ListAsync(user)).Should().BeEmpty($"{kind} não é acerto");
        (await RowsAsync(user)).Should().BeEmpty();
        ShouldBePrimeiroSelect((await PassAsync(user, fx.Ex1)).NewlyUnlockedAchievement);
    }

    [Fact]
    public async Task Backfill_AcertoNaAula2Antiga_SemTentativa_NaoConta_SoAAula1()
    {
        var user = await fx.UserAsync();
        await SeedProgressAsync(user, fx.Lesson2, LessonCompletionKind.ExercisePassed, 7, DateTime.UtcNow.AddDays(-1));

        (await ListAsync(user)).Should().BeEmpty("o backfill por progresso é só da aula seu-primeiro-select");
    }

    [Fact]
    public async Task Alunos_SaoIsolados()
    {
        var a = await fx.UserAsync();
        var b = await fx.UserAsync();

        ShouldBePrimeiroSelect((await PassAsync(a, fx.Ex1)).NewlyUnlockedAchievement);

        (await ListAsync(b)).Should().BeEmpty("o acerto de A não conta para B");
        ShouldBePrimeiroSelect((await PassAsync(b, fx.Ex1)).NewlyUnlockedAchievement);
        (await RowsAsync(a)).Should().ContainSingle();
        (await RowsAsync(b)).Should().ContainSingle();
    }

    [Fact]
    public async Task Selo_NaoDaPontosExtras()
    {
        var user = await fx.UserAsync();

        var pass = await PassAsync(user, fx.Ex1);

        pass.NewlyUnlockedAchievement.Should().NotBeNull();
        await using var db = fx.CreateDbContext();
        var progress = await db.UserProgress.AsNoTracking().Where(p => p.UserId == user).ToListAsync();
        progress.Should().ContainSingle(p => p.LessonId == fx.Lesson1.Id).Which.Score.Should().Be(10, "só os pontos do exercício");
        progress.Sum(p => p.Score ?? 0).Should().Be(10);
    }

    [Fact]
    public async Task DoisPrimeirosAcertosAoMesmoTempo_UmSoAviso_UmaSoLinha()
    {
        for (var round = 0; round < 5; round++)
        {
            var user = await fx.UserAsync();

            var results = await Task.WhenAll(
                PassAsync(user, fx.Ex1, TimeSpan.FromMilliseconds(200)),
                PassAsync(user, fx.Ex2, TimeSpan.FromMilliseconds(200)));

            results.Should().OnlyContain(r => r.Status == ExecutionStatus.Pass);
            results.Count(r => r.NewlyUnlockedAchievement is not null).Should().Be(1, "só quem inseriu a linha avisa");
            (await RowsAsync(user)).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ListAsync_DoMaisAntigoParaOMaisNovo()
    {
        var user = await fx.UserAsync();
        var other = Achievement.CreateWithId(Guid.NewGuid(), "Selo de teste", "Só para ordenar.", """{"slug":"teste-ordem"}""");
        await using (var db = fx.CreateDbContext())
        {
            db.Achievements.Add(other);
            db.UserAchievements.Add(UserAchievement.Grant(user, other.Id));
            await db.SaveChangesAsync();
            await db.UserAchievements.Where(u => u.UserId == user)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EarnedAt, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        await PassAsync(user, fx.Ex1);

        (await ListAsync(user)).Select(a => a.Title).Should().Equal("Selo de teste", "Primeiro SELECT");
    }

    [Fact]
    public async Task ListAsync_AlunoSemSelo_OuInexistente_Vazio()
    {
        (await ListAsync(await fx.UserAsync())).Should().BeEmpty();
        (await ListAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task GrantOnPass_ForaDeTransacao_Recusa()
    {
        await using var db = fx.CreateDbContext();
        var act = () => new AchievementGranter(db).GrantOnPassAsync(Guid.NewGuid(), Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    internal sealed class FakeRunner(string stdout, bool timedOut = false, int exitCode = 0, string stderr = "",
        TimeSpan delay = default) : ICodeRunner
    {
        public string Language => "sql";

        public async Task<SandboxResult> RunAsync(string code, string? preparation = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return new SandboxResult(stdout, stderr, timedOut ? 137 : exitCode, TimeSpan.FromMilliseconds(5),
                timedOut, OomKilled: false, OutputTruncated: false, "fake");
        }
    }
}
