using DevPath.Application.Journey;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Journey;

/// <summary>
/// Um Postgres para a classe. O conteúdo é criado uma vez; cada teste cria o próprio aluno, então os testes
/// não dependem uns dos outros.
/// </summary>
/// <remarks>
/// "SQL & Dados" (ativa):
///   módulo "Primeiros passos" (publicado): Primeiro SELECT · Rascunho A (rascunho) · Aula Oculta (despublicada) · Filtrando com WHERE
///   módulo "Consultas" (publicado): JOIN · GROUP BY
///   módulo "Modulo Oculto" (não publicado): Aula do Oculto
///   módulo "So Rascunhos" (publicado): Rascunho B (rascunho)
/// "Backend NET" (ativa, só rascunhos) · "Trilha Vazia" (ativa, sem aula) · "Trilha Antiga" (desativada).
/// </remarks>
public sealed class StudentJourneyFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public CareerPath Sql { get; private set; } = null!;
    public CareerPath Backend { get; private set; } = null!;
    public CareerPath Empty { get; private set; } = null!;
    public CareerPath Old { get; private set; } = null!;
    public Dictionary<string, Lesson> Lessons { get; } = new();
    public Dictionary<string, Module> Modules { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        Sql = CareerPath.Create("SQL & Dados", "Aprenda SQL do zero.", DifficultyLevel.Beginner, estimatedHours: 20, order: 1);
        var m1 = Add(Sql.AddModule("Primeiros passos"));
        m1.Publish();
        Add(m1.AddLesson("Primeiro SELECT", ContentType.Markdown, "# a"));
        Add(m1.AddLesson("Rascunho A", ContentType.Markdown, "# r", isDraft: true));
        Add(m1.AddLesson("Aula Oculta", ContentType.Markdown, "# o")).Unpublish();
        Add(m1.AddLesson("Filtrando com WHERE", ContentType.Markdown, "# w"));
        var m2 = Add(Sql.AddModule("Consultas"));
        m2.Publish();
        Add(m2.AddLesson("JOIN", ContentType.Markdown, "# j"));
        Add(m2.AddLesson("GROUP BY", ContentType.Markdown, "# g"));
        var hidden = Add(Sql.AddModule("Modulo Oculto"));
        Add(hidden.AddLesson("Aula do Oculto", ContentType.Markdown, "# x"));
        var drafts = Add(Sql.AddModule("So Rascunhos"));
        drafts.Publish();
        Add(drafts.AddLesson("Rascunho B", ContentType.Markdown, "# rb", isDraft: true));

        Backend = CareerPath.Create("Backend NET", "C# e .NET.", DifficultyLevel.Intermediate, estimatedHours: 40, order: 2);
        var bm = Add(Backend.AddModule("Fundamentos C#"));
        bm.Publish();
        Add(bm.AddLesson("Variaveis", ContentType.Markdown, "# v", isDraft: true));

        Empty = CareerPath.Create("Trilha Vazia", order: 3);
        Add(Empty.AddModule("Sem aulas")).Publish();

        Old = CareerPath.Create("Trilha Antiga", order: 4);
        var om = Add(Old.AddModule("Modulo Antigo"));
        om.Publish();
        Add(om.AddLesson("Aula Antiga", ContentType.Markdown, "# z"));
        Old.Deactivate();

        db.CareerPaths.AddRange(Sql, Backend, Empty, Old);
        await db.SaveChangesAsync();
    }

    private Module Add(Module m) => Modules[m.Title] = m;
    private Lesson Add(Lesson l) => Lessons[l.Title] = l;

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task<Guid> CreateUserAsync()
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
        return id;
    }

    public async Task SetProgressAsync(Guid userId, params (string Lesson, ProgressStatus Status)[] items)
    {
        await using var db = CreateDbContext();
        foreach (var (title, status) in items)
        {
            var p = UserProgress.StartLesson(userId, Lessons[title].Id);
            if (status is ProgressStatus.Completed)
            {
                p.Complete();
            }

            db.UserProgress.Add(p);
        }

        await db.SaveChangesAsync();
    }
}

public sealed class StudentJourneyServiceTests(StudentJourneyFixture fx) : IClassFixture<StudentJourneyFixture>
{
    private async Task<T> RunAsync<T>(Func<StudentJourneyService, Task<T>> action)
    {
        await using var db = fx.CreateDbContext();
        return await action(new StudentJourneyService(db));
    }

    private async Task<DashboardDto> EnrolledDashboardAsync(Guid userId, string? slug = null)
    {
        (await RunAsync(s => s.EnrollAsync(userId, slug ?? fx.Sql.Slug))).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return (await RunAsync(s => s.GetDashboardAsync(userId)))!;
    }

    private static DashboardLessonDto Lesson(DashboardDto d, string title) =>
        d.Modules.SelectMany(m => m.Lessons).Single(l => l.Lesson.Title == title);

    // ---------- Escolha de trilha ----------

    [Fact]
    public async Task GetActivePaths_SoAtivas_NaOrdem_ContandoSoAulasDisponiveis()
    {
        var paths = await RunAsync(s => s.GetActivePathsAsync());

        paths.Select(p => p.Title).Should().Equal("SQL & Dados", "Backend NET", "Trilha Vazia");
        paths[0].Should().Be(new PathSummaryDto(fx.Sql.Slug, "SQL & Dados", "Aprenda SQL do zero.", DifficultyLevel.Beginner, 20, 4, 6));
        (paths[1].AvailableLessons, paths[1].TotalLessons).Should().Be((0, 1)); // só rascunho → "Aulas em breve"
        (paths[2].AvailableLessons, paths[2].TotalLessons).Should().Be((0, 0)); // sem aula nenhuma
    }

    [Fact]
    public async Task Enroll_CriaMatricula_E_PreencheTrilhaEModuloAtuais()
    {
        var user = await fx.CreateUserAsync();
        (await RunAsync(s => s.HasCurrentPathAsync(user))).Should().BeFalse();

        var result = await RunAsync(s => s.EnrollAsync(user, $"  {fx.Sql.Slug.ToUpperInvariant()} "));

        result.Should().Be(EnrollmentResult.Enrolled("SQL & Dados"));
        (await RunAsync(s => s.HasCurrentPathAsync(user))).Should().BeTrue();

        await using var db = fx.CreateDbContext();
        (await db.Users.SingleAsync(u => u.Id == user)).CurrentPathId.Should().Be(fx.Sql.Id);
        var enrollment = await db.UserPathEnrollments.SingleAsync(e => e.UserId == user);
        enrollment.CareerPathId.Should().Be(fx.Sql.Id);
        enrollment.CurrentModuleId.Should().Be(fx.Modules["Primeiros passos"].Id);
        enrollment.IsActive.Should().BeTrue();
        enrollment.ProgressPercentage.Should().Be(0);
    }

    [Fact]
    public async Task Enroll_DeNovo_OuEmOutraTrilha_AlreadyEnrolled_SemTrocar()
    {
        var user = await fx.CreateUserAsync();
        await EnrolledDashboardAsync(user);

        (await RunAsync(s => s.EnrollAsync(user, fx.Sql.Slug))).Should().Be(EnrollmentResult.AlreadyEnrolled("SQL & Dados"));

        // Outra trilha: devolve o título da trilha ATUAL. Também vence PathNotFound/NoLessons.
        var other = await RunAsync(s => s.EnrollAsync(user, fx.Backend.Slug));
        other.Should().Be(EnrollmentResult.AlreadyEnrolled("SQL & Dados"));
        other.HasPath.Should().BeTrue();
        (await RunAsync(s => s.EnrollAsync(user, "nao-existe"))).Should().Be(EnrollmentResult.AlreadyEnrolled("SQL & Dados"));
        (await RunAsync(s => s.EnrollAsync(user, fx.Empty.Slug))).Should().Be(EnrollmentResult.AlreadyEnrolled("SQL & Dados"));

        await using var db = fx.CreateDbContext();
        (await db.Users.SingleAsync(u => u.Id == user)).CurrentPathId.Should().Be(fx.Sql.Id);
        (await db.UserPathEnrollments.CountAsync(e => e.UserId == user)).Should().Be(1);
    }

    [Fact]
    public async Task Enroll_SlugInexistenteOuTrilhaDesativada_PathNotFound_TrilhaSemAula_NoLessons()
    {
        var user = await fx.CreateUserAsync();

        (await RunAsync(s => s.EnrollAsync(user, "nao-existe"))).Kind.Should().Be(EnrollmentResultKind.PathNotFound);
        (await RunAsync(s => s.EnrollAsync(user, fx.Old.Slug))).Kind.Should().Be(EnrollmentResultKind.PathNotFound);
        (await RunAsync(s => s.EnrollAsync(user, ""))).Kind.Should().Be(EnrollmentResultKind.PathNotFound);
        (await RunAsync(s => s.EnrollAsync(user, fx.Empty.Slug))).Should().Be(EnrollmentResult.NoLessons());
        EnrollmentResult.NoLessons().HasPath.Should().BeFalse();

        (await RunAsync(s => s.HasCurrentPathAsync(user))).Should().BeFalse();
        await using var db = fx.CreateDbContext();
        (await db.UserPathEnrollments.AnyAsync(e => e.UserId == user)).Should().BeFalse();
    }

    [Fact]
    public async Task UsuarioInexistente_EnrollUserNotFound_HasCurrentPathFalse_DashboardNull()
    {
        var missing = Guid.NewGuid();

        (await RunAsync(s => s.EnrollAsync(missing, fx.Sql.Slug))).Should().Be(EnrollmentResult.UserNotFound());
        (await RunAsync(s => s.EnrollAsync(missing, "nao-existe"))).Should().Be(EnrollmentResult.UserNotFound()); // verificado primeiro
        (await RunAsync(s => s.HasCurrentPathAsync(missing))).Should().BeFalse();
        (await RunAsync(s => s.GetDashboardAsync(missing))).Should().BeNull();

        await using var db = fx.CreateDbContext();
        (await db.UserPathEnrollments.AnyAsync(e => e.UserId == missing)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enroll_PostDuploConcorrente_SoUmMatricula(bool differentPaths)
    {
        for (var round = 0; round < 5; round++)
        {
            var user = await fx.CreateUserAsync();
            var second = differentPaths ? fx.Backend.Slug : fx.Sql.Slug;

            var results = await Task.WhenAll(
                Task.Run(() => RunAsync(s => s.EnrollAsync(user, fx.Sql.Slug))),
                Task.Run(() => RunAsync(s => s.EnrollAsync(user, second))));

            results.Select(r => r.Kind).Should().BeEquivalentTo(
                [EnrollmentResultKind.Enrolled, EnrollmentResultKind.AlreadyEnrolled]);

            // Quem perdeu a corrida recebe o título da trilha de quem ganhou.
            results.Select(r => r.PathTitle).Distinct().Should().ContainSingle();

            await using var db = fx.CreateDbContext();
            var enrollments = await db.UserPathEnrollments.Where(e => e.UserId == user).ToListAsync();
            enrollments.Should().ContainSingle();
            (await db.Users.SingleAsync(u => u.Id == user)).CurrentPathId.Should().Be(enrollments[0].CareerPathId);
        }
    }

    [Fact]
    public async Task Banco_RejeitaSegundaMatriculaAtiva()
    {
        var user = await fx.CreateUserAsync();
        await using var db = fx.CreateDbContext();
        db.UserPathEnrollments.AddRange(UserPathEnrollment.Enroll(user, fx.Sql.Id), UserPathEnrollment.Enroll(user, fx.Backend.Id));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_UserPathEnrollments_UserId_Active");
    }

    // ---------- Dashboard ----------

    [Fact]
    public async Task Dashboard_SemTrilha_Null()
    {
        var user = await fx.CreateUserAsync();
        (await RunAsync(s => s.GetDashboardAsync(user))).Should().BeNull();
        (await RunAsync(s => s.GetDashboardAsync(Guid.NewGuid()))).Should().BeNull();
    }

    [Fact]
    public async Task Dashboard_PrimeiroAcesso_EscondeDespublicados_RascunhoEmBreve_BloqueioComRequisito()
    {
        var user = await fx.CreateUserAsync();
        var d = await EnrolledDashboardAsync(user);

        d.PathSlug.Should().Be(fx.Sql.Slug);
        d.PathTitle.Should().Be("SQL & Dados");
        d.IsFirstAccess.Should().BeTrue();
        d.NextLesson.Should().Be(new LessonRefDto(fx.Sql.Slug, fx.Modules["Primeiros passos"].Slug, fx.Lessons["Primeiro SELECT"].Slug, "Primeiro SELECT"));

        // Módulo e aula despublicados somem; módulo só de rascunho aparece (Em breve).
        d.Modules.Select(m => m.Title).Should().Equal("Primeiros passos", "Consultas", "So Rascunhos");
        d.Modules[0].Lessons.Select(l => l.Lesson.Title).Should().Equal("Primeiro SELECT", "Rascunho A", "Filtrando com WHERE");

        d.AvailableLessons.Should().Be(4);
        d.TotalLessons.Should().Be(6);
        d.CompletedLessons.Should().Be(0);
        d.ProgressPercentage.Should().Be(0);

        Lesson(d, "Primeiro SELECT").Status.Should().Be(JourneyLessonStatus.Available);
        Lesson(d, "Rascunho A").Status.Should().Be(JourneyLessonStatus.ComingSoon);
        Lesson(d, "Rascunho B").Status.Should().Be(JourneyLessonStatus.ComingSoon);

        var where = Lesson(d, "Filtrando com WHERE");
        where.Status.Should().Be(JourneyLessonStatus.Locked);
        where.UnlockRequirement!.Title.Should().Be("Primeiro SELECT"); // o rascunho no meio não bloqueia

        var join = Lesson(d, "JOIN");
        join.Status.Should().Be(JourneyLessonStatus.Locked);
        join.UnlockRequirement.Should().Be(new LessonRefDto(fx.Sql.Slug, fx.Modules["Primeiros passos"].Slug,
            fx.Lessons["Filtrando com WHERE"].Slug, "Filtrando com WHERE"));
        Lesson(d, "GROUP BY").UnlockRequirement!.Title.Should().Be("JOIN");

        var drafts = d.Modules.Single(m => m.Title == "So Rascunhos");
        (drafts.AvailableLessons, drafts.CompletedLessons, drafts.ProgressPercentage, drafts.TotalLessons).Should().Be((0, 0, 0, 1));
    }

    [Fact]
    public async Task Dashboard_TodosOsStatus_E_PercentualSemRascunho()
    {
        var user = await fx.CreateUserAsync();
        await EnrolledDashboardAsync(user);
        await fx.SetProgressAsync(user,
            ("Primeiro SELECT", ProgressStatus.Completed),
            ("Filtrando com WHERE", ProgressStatus.InProgress),
            ("Rascunho A", ProgressStatus.Completed)); // rascunho concluído não conta (R4)

        var d = (await RunAsync(s => s.GetDashboardAsync(user)))!;

        d.IsFirstAccess.Should().BeFalse();
        d.CompletedLessons.Should().Be(1);
        d.AvailableLessons.Should().Be(4);
        d.ProgressPercentage.Should().Be(25);
        d.Modules[0].ProgressPercentage.Should().Be(50);
        d.Modules[1].ProgressPercentage.Should().Be(0);

        Lesson(d, "Primeiro SELECT").Status.Should().Be(JourneyLessonStatus.Completed);
        Lesson(d, "Rascunho A").Status.Should().Be(JourneyLessonStatus.ComingSoon);
        Lesson(d, "Filtrando com WHERE").Status.Should().Be(JourneyLessonStatus.InProgress);
        Lesson(d, "JOIN").Status.Should().Be(JourneyLessonStatus.Locked);
        Lesson(d, "JOIN").UnlockRequirement!.Title.Should().Be("Filtrando com WHERE");
        d.NextLesson!.Title.Should().Be("Filtrando com WHERE");

        await fx.SetProgressAsync(user, ("Aula Oculta", ProgressStatus.Completed)); // despublicada: não conta
        await using (var db = fx.CreateDbContext())
        {
            var where = await db.UserProgress.SingleAsync(p => p.UserId == user && p.LessonId == fx.Lessons["Filtrando com WHERE"].Id);
            where.Complete();
            await db.SaveChangesAsync();
        }

        d = (await RunAsync(s => s.GetDashboardAsync(user)))!;
        d.CompletedLessons.Should().Be(2);
        d.ProgressPercentage.Should().Be(50);
        Lesson(d, "JOIN").Status.Should().Be(JourneyLessonStatus.Available);
        Lesson(d, "GROUP BY").Status.Should().Be(JourneyLessonStatus.Locked);
        d.NextLesson!.Title.Should().Be("JOIN");
    }

    [Fact]
    public async Task Dashboard_TudoConcluido_AllDone()
    {
        var user = await fx.CreateUserAsync();
        await EnrolledDashboardAsync(user);
        await fx.SetProgressAsync(user,
            ("Primeiro SELECT", ProgressStatus.Completed), ("Filtrando com WHERE", ProgressStatus.Completed),
            ("JOIN", ProgressStatus.Completed), ("GROUP BY", ProgressStatus.Completed));

        var d = (await RunAsync(s => s.GetDashboardAsync(user)))!;

        d.NextLesson.Should().BeNull();
        d.AvailableLessons.Should().Be(4);
        d.ProgressPercentage.Should().Be(100);
        d.Modules.Where(m => m.AvailableLessons > 0).Should().OnlyContain(m => m.ProgressPercentage == 100);
    }

    [Fact]
    public async Task Dashboard_TrilhaSoComRascunhos_Vazia()
    {
        var user = await fx.CreateUserAsync();
        var d = await EnrolledDashboardAsync(user, fx.Backend.Slug);

        d.PathTitle.Should().Be("Backend NET");
        d.AvailableLessons.Should().Be(0);
        d.ProgressPercentage.Should().Be(0);
        d.NextLesson.Should().BeNull();
        d.IsFirstAccess.Should().BeTrue();
        d.Modules.Should().ContainSingle().Which.Lessons.Should().ContainSingle()
            .Which.Status.Should().Be(JourneyLessonStatus.ComingSoon);

        await using var db = fx.CreateDbContext();
        (await db.UserPathEnrollments.SingleAsync(e => e.UserId == user)).CurrentModuleId.Should().Be(fx.Modules["Fundamentos C#"].Id);
    }

    [Fact]
    public async Task Dashboard_ProgressoDeOutraTrilha_NaoContaNemTiraPrimeiroAcesso()
    {
        var user = await fx.CreateUserAsync();
        await EnrolledDashboardAsync(user);
        await fx.SetProgressAsync(user, ("Aula Antiga", ProgressStatus.Completed));

        var d = (await RunAsync(s => s.GetDashboardAsync(user)))!;

        d.IsFirstAccess.Should().BeTrue();
        d.CompletedLessons.Should().Be(0);
    }
}
