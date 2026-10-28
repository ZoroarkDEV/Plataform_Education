using DevPath.Application.Journey;
using DevPath.Domain.Common;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPath.UnitTests.Web.Onboarding;

/// <summary>Adapter <see cref="StudentJourneyGateway"/>: mapeamento DTO → view model e os casos de borda do contrato.</summary>
public class StudentJourneyGatewayTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Path = "sql-dados";

    private static LessonRefDto Ref(string module, string lesson, string title) => new(Path, module, lesson, title);

    [Fact]
    public async Task Trilhas_MapeiaCampoACampo_NaOrdemDoServico()
    {
        var service = new StubService
        {
            Paths =
            [
                new(Path, "SQL & Dados", "SQL do zero.", DifficultyLevel.Beginner, 40, 5, 7),
                new("backend-net", "Backend .NET", "C# e .NET.", DifficultyLevel.Intermediate, 120, 0, 3)
            ]
        };

        var paths = await Gateway(service).GetActivePathsAsync(CancellationToken.None);

        paths.Should().Equal(
            new PathOptionViewModel(Path, "SQL & Dados", "SQL do zero.", DifficultyLevel.Beginner, 40, 5),
            new PathOptionViewModel("backend-net", "Backend .NET", "C# e .NET.", DifficultyLevel.Intermediate, 120, 0));
        paths[1].LessonsLabel.Should().Be("Aulas em breve");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TemTrilha_RepassaOServico(bool hasPath)
    {
        (await Gateway(new StubService { HasPath = hasPath }).HasCurrentPathAsync(UserId, CancellationToken.None))
            .Should().Be(hasPath);
    }

    public static TheoryData<EnrollmentResult, EnrollmentOutcome> Matriculas => new()
    {
        { EnrollmentResult.Enrolled("SQL & Dados"), new EnrollmentOutcome(EnrollmentStatus.Enrolled, "SQL & Dados") },
        // AlreadyEnrolled traz o título da trilha ATUAL do aluno.
        { EnrollmentResult.AlreadyEnrolled("SQL & Dados"), new EnrollmentOutcome(EnrollmentStatus.AlreadyEnrolled, "SQL & Dados") },
        { EnrollmentResult.PathNotFound(), new EnrollmentOutcome(EnrollmentStatus.PathNotFound) },
        // NoLessons = trilha ativa sem aula disponível (inclusive só rascunhos), NÃO serviço fora do ar.
        { EnrollmentResult.NoLessons(), new EnrollmentOutcome(EnrollmentStatus.NoLessons) },
        // Conta apagada com cookie válido: o serviço devolve UserNotFound em vez de lançar.
        { EnrollmentResult.UserNotFound(), new EnrollmentOutcome(EnrollmentStatus.UserNotFound) }
    };

    [Theory]
    [MemberData(nameof(Matriculas))]
    public async Task Matricula_MapeiaOKind(EnrollmentResult result, EnrollmentOutcome expected)
    {
        var service = new StubService { Enroll = (_, _) => Task.FromResult(result) };

        (await Gateway(service).EnrollAsync(UserId, Path, CancellationToken.None)).Should().Be(expected);
        service.EnrolledWith.Should().Be((UserId, Path));
    }

    [Fact]
    public void Matricula_TodoKindDoBackendTemMapeamento()
    {
        foreach (var kind in Enum.GetValues<EnrollmentResultKind>())
        {
            var act = () => StudentJourneyGateway.ToOutcome(new EnrollmentResult(kind, "T"));
            act.Should().NotThrow($"EnrollmentResultKind.{kind} precisa de um caso no adapter");
        }
    }

    [Fact]
    public void Matricula_SemTitulo_NaoInventaTitulo()
    {
        StudentJourneyGateway.ToOutcome(new EnrollmentResult(EnrollmentResultKind.AlreadyEnrolled))
            .Should().Be(new EnrollmentOutcome(EnrollmentStatus.AlreadyEnrolled));
        StudentJourneyGateway.ToOutcome(new EnrollmentResult(EnrollmentResultKind.Enrolled))
            .Should().Be(new EnrollmentOutcome(EnrollmentStatus.Enrolled));
    }

    [Fact]
    public async Task Matricula_ExcecoesNaoSaoEngolidas()
    {
        // Sem o catch amplo de InvalidOperationException: o contrato novo não lança para casos esperados,
        // então uma exceção é falha de verdade (inclusive InvalidOperationException do EF).
        var timeout = new StubService { Enroll = (_, _) => throw new TimeoutException() };
        await ((Func<Task>)(() => Gateway(timeout).EnrollAsync(UserId, Path, CancellationToken.None)))
            .Should().ThrowAsync<TimeoutException>();

        var invalid = new StubService { Enroll = (_, _) => throw new InvalidOperationException("falha do EF") };
        await ((Func<Task>)(() => Gateway(invalid).EnrollAsync(UserId, Path, CancellationToken.None)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Dashboard_Nulo_ViraNulo()
    {
        (await Gateway(new StubService()).GetDashboardAsync(UserId, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(JourneyLessonStatus.Completed, LessonStatus.Completed)]
    [InlineData(JourneyLessonStatus.InProgress, LessonStatus.InProgress)]
    [InlineData(JourneyLessonStatus.Available, LessonStatus.Available)]
    [InlineData(JourneyLessonStatus.Locked, LessonStatus.Locked)]
    [InlineData(JourneyLessonStatus.ComingSoon, LessonStatus.ComingSoon)]
    public void Status_MapeiaPeloNome_NaoPeloNumero(JourneyLessonStatus source, LessonStatus expected) =>
        StudentJourneyGateway.ToViewModel(source).Should().Be(expected);

    [Fact]
    public void Status_TodoMembroDoBackendTemMapeamento()
    {
        Enum.GetValues<JourneyLessonStatus>().Select(StudentJourneyGateway.ToViewModel)
            .Should().OnlyHaveUniqueItems().And.HaveCount(Enum.GetValues<LessonStatus>().Length);
    }

    [Fact]
    public async Task Dashboard_MapeiaCampoACampo_ComMotivoDoBloqueioEProximaAula()
    {
        var select = Ref("primeiros-passos", "seu-primeiro-select", "Seu primeiro SELECT");
        var where = Ref("primeiros-passos", "filtrando-com-where", "Filtrando com WHERE");
        var join = Ref("primeiros-passos", "juntando-tabelas", "Juntando tabelas");
        var draft = Ref("primeiros-passos", "rascunho", "Rascunho");
        var dto = new DashboardDto(Path, "SQL & Dados", 1, 3, 33, where, false,
        [
            new DashboardModuleDto("primeiros-passos", "Primeiros passos com SQL", 1, 3, 33,
            [
                new(select, JourneyLessonStatus.Completed),
                new(where, JourneyLessonStatus.InProgress),
                new(draft, JourneyLessonStatus.ComingSoon),
                new(join, JourneyLessonStatus.Locked, where)
            ])
        ]);

        var vm = (await Gateway(new StubService { Dashboard = dto }).GetDashboardAsync(UserId, CancellationToken.None))!;

        vm.PathSlug.Should().Be(Path);
        vm.PathTitle.Should().Be("SQL & Dados");
        vm.NextLesson.Should().Be(new LessonRef(Path, "primeiros-passos", "filtrando-com-where", "Filtrando com WHERE"));
        vm.NextLesson!.Url.Should().Be("/trilhas/sql-dados/primeiros-passos/filtrando-com-where");
        vm.IsFirstAccess.Should().BeFalse();
        vm.State.Should().Be(DashboardState.Continue);

        var module = vm.Modules.Should().ContainSingle().Subject;
        module.Slug.Should().Be("primeiros-passos");
        module.Title.Should().Be("Primeiros passos com SQL");
        module.Lessons.Select(l => l.Status).Should().Equal(
            LessonStatus.Completed, LessonStatus.InProgress, LessonStatus.ComingSoon, LessonStatus.Locked);
        module.Lessons[3].UnlockRequirement.Should().Be(new LessonRef(Path, "primeiros-passos", "filtrando-com-where", "Filtrando com WHERE"));
        module.Lessons.Take(3).Should().OnlyContain(l => l.UnlockRequirement == null);
        module.Lessons[0].Lesson.Title.Should().Be("Seu primeiro SELECT");
    }

    [Fact]
    public async Task Dashboard_PercentualEXdeY_UsamAsAulasDisponiveis_NaoOTotalComRascunhos()
    {
        var a = Ref("m", "a", "A");
        var dto = new DashboardDto(Path, "SQL & Dados", 1, 2, 50, Ref("m", "b", "B"), false,
        [
            new DashboardModuleDto("m", "Módulo", 1, 2, 50,
            [
                new(a, JourneyLessonStatus.Completed),
                new(Ref("m", "b", "B"), JourneyLessonStatus.Available),
                new(Ref("m", "r", "R"), JourneyLessonStatus.ComingSoon)
            ])
        ]);
        dto.TotalLessons.Should().Be(3, "o TotalLessons do Backend conta o rascunho");

        var vm = (await Gateway(new StubService { Dashboard = dto }).GetDashboardAsync(UserId, CancellationToken.None))!;

        vm.ProgressLabel.Should().Be("50% concluído — 1 de 2 aulas");
        vm.Modules[0].ProgressLabel.Should().Be("50% concluído — 1 de 2 aulas");
        vm.Modules[0].Lessons.Should().HaveCount(3, "o rascunho aparece no roadmap como \"Em breve\"");
    }

    [Fact]
    public async Task Dashboard_TrilhaSoComRascunhos_CaiNoEstadoVazio()
    {
        var dto = new DashboardDto(Path, "SQL & Dados", 0, 0, 0, null, true,
        [
            new DashboardModuleDto("m", "Módulo", 0, 0, 0, [new(Ref("m", "r", "R"), JourneyLessonStatus.ComingSoon)])
        ]);

        var vm = (await Gateway(new StubService { Dashboard = dto }).GetDashboardAsync(UserId, CancellationToken.None))!;

        vm.State.Should().Be(DashboardState.Empty);
        vm.Modules[0].IsComingSoon.Should().BeTrue();
        vm.Modules[0].ProgressLabel.Should().Be("Em breve");
    }

    [Fact]
    public async Task Dashboard_TudoConcluido_SemProximaAula()
    {
        var dto = new DashboardDto(Path, "SQL & Dados", 1, 1, 100, null, false,
            [new DashboardModuleDto("m", "Módulo", 1, 1, 100, [new(Ref("m", "a", "A"), JourneyLessonStatus.Completed)])]);

        var vm = (await Gateway(new StubService { Dashboard = dto }).GetDashboardAsync(UserId, CancellationToken.None))!;

        vm.State.Should().Be(DashboardState.AllDone);
        vm.NextLesson.Should().BeNull();
    }

    private static StudentJourneyGateway Gateway(StubService service) =>
        new(service, NullLogger<StudentJourneyGateway>.Instance);

    private sealed class StubService : IStudentJourneyService
    {
        public IReadOnlyList<PathSummaryDto> Paths { get; init; } = [];
        public bool HasPath { get; init; }
        public DashboardDto? Dashboard { get; init; }
        public Func<Guid, string, Task<EnrollmentResult>> Enroll { get; init; } =
            (_, _) => Task.FromResult(EnrollmentResult.PathNotFound());
        public (Guid, string)? EnrolledWith { get; private set; }

        public Task<IReadOnlyList<PathSummaryDto>> GetActivePathsAsync(CancellationToken ct = default) => Task.FromResult(Paths);
        public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(HasPath);

        public Task<EnrollmentResult> EnrollAsync(Guid userId, string pathSlug, CancellationToken ct = default)
        {
            EnrolledWith = (userId, pathSlug);
            return Enroll(userId, pathSlug);
        }

        public Task<DashboardDto?> GetDashboardAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Dashboard);
    }
}
