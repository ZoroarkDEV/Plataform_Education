using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Persistence;
using DevPath.Infrastructure.Progress;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Progress;

/// <summary>
/// "Progresso" (ativa):
///   módulo "Basico": Leitura (só leitura) · Exercicio (1 exercício, 10 pts) · Leitura 2 (só leitura)
///   módulo "Avancado": Dois exercicios (5 + 7 pts)
/// "Outra trilha" (ativa): Outra aula. Cada teste cria o próprio aluno.
/// </summary>
public sealed class LessonProgressFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private const string Cases = """[{"name":"c","expected":{"columns":["x"],"rows":[["1"]]},"orderMatters":false}]""";

    public CareerPath Path { get; private set; } = null!;
    public CareerPath Other { get; private set; } = null!;
    public Module Basic { get; private set; } = null!;
    public Module Advanced { get; private set; } = null!;
    public Lesson Reading { get; private set; } = null!;
    public Lesson WithExercise { get; private set; } = null!;
    public Lesson Reading2 { get; private set; } = null!;
    public Lesson TwoExercises { get; private set; } = null!;
    public Exercise Exercise { get; private set; } = null!;
    public Exercise ExerciseA { get; private set; } = null!;
    public Exercise ExerciseB { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        Path = CareerPath.Create("Progresso", order: 1);
        Basic = Path.AddModule("Basico");
        Basic.Publish();
        Reading = Basic.AddLesson("Leitura", ContentType.Markdown, "# l");
        WithExercise = Basic.AddLesson("Exercicio", ContentType.Markdown, "# e");
        Exercise = WithExercise.AddExercise("E", "x", "sql", Cases, points: 10);
        Reading2 = Basic.AddLesson("Leitura 2", ContentType.Markdown, "# l2");
        Advanced = Path.AddModule("Avancado");
        Advanced.Publish();
        TwoExercises = Advanced.AddLesson("Dois exercicios", ContentType.Markdown, "# d");
        ExerciseA = TwoExercises.AddExercise("A", "x", "sql", Cases, points: 5);
        ExerciseB = TwoExercises.AddExercise("B", "x", "sql", Cases, points: 7);

        Other = CareerPath.Create("Outra trilha", order: 2);
        var om = Other.AddModule("Outro");
        om.Publish();
        om.AddLesson("Outra aula", ContentType.Markdown, "# o");

        db.CareerPaths.AddRange(Path, Other);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task<Guid> EnrolledUserAsync(CareerPath? path = null)
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid();
        var email = $"aluno-{id:N}@devpath.test";
        db.Users.Add(new ApplicationUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = "Aluno",
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();
        (await new StudentJourneyService(db, new JourneySnapshotLoader(db)).EnrollAsync(id, (path ?? Path).Slug))
            .Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return id;
    }

    public async Task SubmitAsync(Guid userId, Exercise exercise, SubmissionResult result, int times = 1)
    {
        await using var db = CreateDbContext();
        for (var i = 0; i < times; i++)
        {
            db.CodeSubmissions.Add(CodeSubmission.Create(userId, exercise.Id, "SELECT 1;", result, 1));
        }

        await db.SaveChangesAsync();
    }
}

public sealed class LessonProgressServiceTests(LessonProgressFixture fx) : IClassFixture<LessonProgressFixture>
{
    private async Task<T> RunAsync<T>(Func<LessonProgressService, Task<T>> action)
    {
        await using var db = fx.CreateDbContext();
        var loader = new JourneySnapshotLoader(db);
        return await action(new LessonProgressService(db, loader, new LessonProgressWriter(db, loader)));
    }

    private Task<LessonCompletionResult> CompleteAsync(Guid userId, Lesson lesson) =>
        RunAsync(s => s.CompleteAsync(userId, fx.Path.Slug, ModuleOf(lesson).Slug, lesson.Slug));

    private Task<LessonAccessResult> OpenAsync(Guid userId, Lesson lesson) =>
        RunAsync(s => s.OpenAsync(userId, fx.Path.Slug, ModuleOf(lesson).Slug, lesson.Slug));

    private Task<LessonAccessResult> StateAsync(Guid userId, Lesson lesson) =>
        RunAsync(s => s.GetStateAsync(userId, fx.Path.Slug, ModuleOf(lesson).Slug, lesson.Slug));

    private Module ModuleOf(Lesson lesson) => lesson.ModuleId == fx.Basic.Id ? fx.Basic : fx.Advanced;

    private async Task<UserPathEnrollment> EnrollmentAsync(Guid userId)
    {
        await using var db = fx.CreateDbContext();
        return await db.UserPathEnrollments.AsNoTracking().SingleAsync(e => e.UserId == userId && e.IsActive);
    }

    private async Task<List<UserProgress>> RowsAsync(Guid userId, Lesson lesson)
    {
        await using var db = fx.CreateDbContext();
        return await db.UserProgress.AsNoTracking().Where(p => p.UserId == userId && p.LessonId == lesson.Id).ToListAsync();
    }

    private async Task<bool> PassOnWorkerAsync(Guid userId, Exercise exercise)
    {
        await fx.SubmitAsync(userId, exercise, SubmissionResult.Pass);
        await using var db = fx.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var done = await new LessonProgressWriter(db, new JourneySnapshotLoader(db)).CompleteOnPassAsync(userId, exercise.Id);
        await tx.CommitAsync();
        return done;
    }

    [Fact]
    public async Task ConcluirLeitura_LiberaAProxima_ERecalculaAMatricula()
    {
        var user = await fx.EnrolledUserAsync();
        (await StateAsync(user, fx.WithExercise)).Kind.Should().Be(LessonAccessKind.Locked);

        var result = await CompleteAsync(user, fx.Reading);

        result.Kind.Should().Be(LessonCompletionResultKind.Completed);
        result.State!.CompletionKind.Should().Be(LessonCompletionKind.Read);
        result.State.Points.Should().BeNull("aula só de leitura não dá pontos (Q5)");
        result.ProgressPercentage.Should().Be(25);
        result.State.Next!.Lesson.LessonSlug.Should().Be(fx.WithExercise.Slug);
        result.State.Next.Status.Should().Be(JourneyLessonStatus.Available);
        (await StateAsync(user, fx.WithExercise)).Kind.Should().Be(LessonAccessKind.Allowed);
        (await EnrollmentAsync(user)).ProgressPercentage.Should().Be(25m);
    }

    [Fact]
    public async Task AulaBloqueada_NaoConcluiNemGravaAoAbrir()
    {
        var user = await fx.EnrolledUserAsync();

        (await CompleteAsync(user, fx.Reading2)).Kind.Should().Be(LessonCompletionResultKind.Locked);
        var open = await OpenAsync(user, fx.Reading2);

        open.Kind.Should().Be(LessonAccessKind.Locked);
        open.State!.UnlockRequirement!.LessonSlug.Should().Be(fx.WithExercise.Slug);
        (await RowsAsync(user, fx.Reading2)).Should().BeEmpty();
    }

    [Fact]
    public async Task SemMatriculaOuOutraTrilha_NaoConclui()
    {
        var other = await fx.EnrolledUserAsync(fx.Other);

        (await CompleteAsync(other, fx.Reading)).Kind.Should().Be(LessonCompletionResultKind.NotEnrolled);
        var open = await OpenAsync(other, fx.Reading);
        open.Kind.Should().Be(LessonAccessKind.NotEnrolled);
        open.CurrentPathTitle.Should().Be("Outra trilha");
        (await RunAsync(s => s.CompleteAsync(Guid.NewGuid(), fx.Path.Slug, fx.Basic.Slug, fx.Reading.Slug)))
            .Kind.Should().Be(LessonCompletionResultKind.UserNotFound);
        (await RunAsync(s => s.CompleteAsync(other, fx.Path.Slug, fx.Basic.Slug, "nao-existe")))
            .Kind.Should().Be(LessonCompletionResultKind.LessonNotFound);
    }

    [Fact]
    public async Task ConcluirDeNovo_DevolveAlreadyCompleted()
    {
        var user = await fx.EnrolledUserAsync();
        await CompleteAsync(user, fx.Reading);

        (await CompleteAsync(user, fx.Reading)).Kind.Should().Be(LessonCompletionResultKind.AlreadyCompleted);
        (await RowsAsync(user, fx.Reading)).Should().ContainSingle();
    }

    [Fact]
    public async Task ExercicioPendente_RequirementNotMet_MostraTentativasSemLimite()
    {
        var user = await fx.EnrolledUserAsync();
        await CompleteAsync(user, fx.Reading);
        await fx.SubmitAsync(user, fx.Exercise, SubmissionResult.Fail, 4);

        var result = await CompleteAsync(user, fx.WithExercise);

        result.Kind.Should().Be(LessonCompletionResultKind.RequirementNotMet);
        result.State!.Blocker.Should().Be(LessonCompletionBlocker.ExerciseNotPassed);
        var ex = result.State.Exercise!;
        ex.MaxAttempts.Should().BeNull("null = execute quantas vezes quiser");
        ex.RemainingAttempts.Should().BeNull();
        ex.FailedAttempts.Should().Be(4);
        ex.FailuresUntilSkip.Should().Be(1);
        ex.CanSkip.Should().BeFalse();
        (await RowsAsync(user, fx.WithExercise)).Should().BeEmpty();
    }

    [Fact]
    public async Task CincoFalhas_SeguirSemPontos_DepoisPassPromoveParaPontosCheios()
    {
        var user = await fx.EnrolledUserAsync();
        await CompleteAsync(user, fx.Reading);
        await fx.SubmitAsync(user, fx.Exercise, SubmissionResult.Fail, LessonCompletionRule.DefaultFailuresBeforeSkip);
        (await StateAsync(user, fx.WithExercise)).State!.Exercise!.CanSkip.Should().BeTrue();

        var skip = await CompleteAsync(user, fx.WithExercise);

        skip.Kind.Should().Be(LessonCompletionResultKind.Completed);
        skip.State!.CompletionKind.Should().Be(LessonCompletionKind.SkippedAfterFailures);
        skip.State.Points.Should().Be(0);
        skip.ProgressPercentage.Should().Be(50);
        var skippedAt = (await RowsAsync(user, fx.WithExercise)).Single().CompletedAt;

        (await PassOnWorkerAsync(user, fx.Exercise)).Should().BeTrue("o pass depois do pular promove");

        var row = (await RowsAsync(user, fx.WithExercise)).Single();
        row.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        row.Score.Should().Be(10, "pass sempre dá os pontos cheios, sem desconto pelas falhas");
        row.CompletedAt.Should().Be(skippedAt);
        (await EnrollmentAsync(user)).ProgressPercentage.Should().Be(50m);
        (await PassOnWorkerAsync(user, fx.Exercise)).Should().BeFalse("já tem os pontos");
    }

    [Fact]
    public async Task PassNoWorker_SoConcluiQuandoTodosOsExerciciosDaAulaPassaram()
    {
        var user = await fx.EnrolledUserAsync();
        await CompleteAsync(user, fx.Reading);
        (await PassOnWorkerAsync(user, fx.Exercise)).Should().BeTrue();
        await CompleteAsync(user, fx.Reading2);

        (await PassOnWorkerAsync(user, fx.ExerciseA)).Should().BeFalse("B ainda pendente");
        (await PassOnWorkerAsync(user, fx.ExerciseB)).Should().BeTrue();

        var row = (await RowsAsync(user, fx.TwoExercises)).Single();
        row.Score.Should().Be(12);
        var enrollment = await EnrollmentAsync(user);
        enrollment.ProgressPercentage.Should().Be(100m);
    }

    [Fact]
    public async Task PassEmAulaBloqueada_NaoConclui()
    {
        var user = await fx.EnrolledUserAsync();

        (await PassOnWorkerAsync(user, fx.Exercise)).Should().BeFalse();
        (await RowsAsync(user, fx.WithExercise)).Should().BeEmpty();
    }

    [Fact]
    public async Task Abrir_GravaInProgress_EContinuarVaiParaAUltimaAberta()
    {
        var user = await fx.EnrolledUserAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var open = await OpenAsync(user, fx.Reading);

        open.Kind.Should().Be(LessonAccessKind.Allowed);
        open.State!.Status.Should().Be(JourneyLessonStatus.InProgress);
        var row = (await RowsAsync(user, fx.Reading)).Single();
        row.Status.Should().Be(ProgressStatus.InProgress);
        row.LastAccessedAt.Should().BeAfter(before);

        await CompleteAsync(user, fx.Reading);
        await OpenAsync(user, fx.WithExercise);
        await OpenAsync(user, fx.Reading); // reabrir concluída não volta para InProgress
        (await RowsAsync(user, fx.Reading)).Single().Status.Should().Be(ProgressStatus.Completed);

        await using var db = fx.CreateDbContext();
        var dashboard = await new StudentJourneyService(db, new JourneySnapshotLoader(db)).GetDashboardAsync(user);
        dashboard!.NextLesson!.LessonSlug.Should().Be(fx.WithExercise.Slug, "Continuar = a última aula aberta não concluída (Q6)");
    }

    [Fact]
    public async Task CliquesSimultaneos_UmaLinhaSo_EPercentualCerto()
    {
        var user = await fx.EnrolledUserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => CompleteAsync(user, fx.Reading))));

        results.Count(r => r.Kind == LessonCompletionResultKind.Completed).Should().Be(1);
        results.Count(r => r.Kind == LessonCompletionResultKind.AlreadyCompleted).Should().Be(7);
        (await RowsAsync(user, fx.Reading)).Should().ContainSingle();
        (await EnrollmentAsync(user)).ProgressPercentage.Should().Be(25m);
    }

    [Fact]
    public async Task ConclusoesSimultaneasDeAulasDiferentes_NaoPerdemOPercentual()
    {
        // Worker promovendo a aula 2 (pular → pass) enquanto o aluno conclui a aula 3: o recálculo de um
        // não pode sobrescrever o do outro com um snapshot velho.
        var users = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var user = await fx.EnrolledUserAsync();
            await CompleteAsync(user, fx.Reading);
            await fx.SubmitAsync(user, fx.Exercise, SubmissionResult.Fail, 5);
            await CompleteAsync(user, fx.WithExercise);
            users.Add(user);
        }

        await Task.WhenAll(users.SelectMany(u => new Task[]
        {
            Task.Run(() => PassOnWorkerAsync(u, fx.Exercise)),
            Task.Run(() => CompleteAsync(u, fx.Reading2))
        }));

        foreach (var user in users)
        {
            var enrollment = await EnrollmentAsync(user);
            enrollment.ProgressPercentage.Should().Be(75m);
            enrollment.CurrentModuleId.Should().Be(fx.Advanced.Id);
            (await RowsAsync(user, fx.WithExercise)).Single().CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        }
    }

    [Fact]
    public async Task VoltarParaATrilha_RecalculaOPercentual()
    {
        // Trocar de trilha ainda está fora do #11; aqui simulamos "saiu e voltou" limpando CurrentPathId
        // e desativando a matrícula, para exercitar o recálculo na reativação (ADR-007 §4).
        var user = await fx.EnrolledUserAsync();
        await CompleteAsync(user, fx.Reading);
        await using var db = fx.CreateDbContext();
        await db.Users.Where(u => u.Id == user).ExecuteUpdateAsync(s => s.SetProperty(u => u.CurrentPathId, (Guid?)null));
        await db.UserPathEnrollments.Where(e => e.UserId == user && e.CareerPathId == fx.Path.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsActive, false)
                .SetProperty(e => e.ProgressPercentage, 0m)); // % velho de propósito
        var journey = new StudentJourneyService(db, new JourneySnapshotLoader(db));
        (await journey.EnrollAsync(user, fx.Path.Slug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);

        (await EnrollmentAsync(user)).ProgressPercentage.Should().Be(25m);
    }
}
