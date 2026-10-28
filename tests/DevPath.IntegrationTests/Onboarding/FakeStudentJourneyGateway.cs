using System.Collections.Concurrent;
using DevPath.Domain.Common;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.IntegrationTests.Onboarding;

/// <summary>
/// DUBLÊ DE TESTE (só existe neste projeto de testes): faz o papel do serviço de onboarding/progresso
/// que o Backend ainda vai implementar. Guarda a matrícula em memória e devolve um dashboard de exemplo.
/// </summary>
public sealed class FakeStudentJourneyGateway : IStudentJourneyGateway
{
    public const string SqlPath = "sql-dados";

    private readonly ConcurrentDictionary<Guid, string> _enrollments = new();

    public IReadOnlyList<PathOptionViewModel> Paths { get; init; } =
    [
        new(SqlPath, "SQL & Dados",
            "SQL do zero com PostgreSQL: consultas, modelagem e como conectar o C# ao banco.",
            DifficultyLevel.Beginner, 40, 5),
        new("backend-net", "Backend .NET",
            "C#, ASP.NET Core MVC e Entity Framework Core para construir APIs e sites.",
            DifficultyLevel.Beginner, 120, 0)
    ];

    /// <summary>Monta o dashboard que o "serviço" devolve para quem já escolheu trilha.</summary>
    public Func<string, DashboardViewModel> Dashboard { get; init; } = _ => Samples.FirstAccess();

    public Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Paths);

    public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_enrollments.ContainsKey(userId));

    public Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken)
    {
        var path = Paths.FirstOrDefault(p => p.Slug == pathSlug);
        if (path is null)
        {
            return Task.FromResult(EnrollmentOutcome.PathNotFound());
        }

        return Task.FromResult(_enrollments.TryAdd(userId, pathSlug)
            ? EnrollmentOutcome.Enrolled(path.Title)
            : EnrollmentOutcome.AlreadyEnrolled());
    }

    public Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_enrollments.TryGetValue(userId, out var slug) ? Dashboard(slug) : null);

    /// <summary>
    /// Dashboards de exemplo (trilha SQL &amp; Dados) para cada estado da US-11.3. Slugs, títulos e ordem
    /// iguais ao <c>/content</c> e às decisões do #22: "Primeiros passos com SQL" é o módulo 1, com
    /// JOIN antes de GROUP BY; os módulos antigos descem uma posição. Rascunhos ("Em breve") não contam
    /// no total de aulas (R4), por isso a trilha tem 5 aulas disponíveis.
    /// </summary>
    public static class Samples
    {
        public const string PrimeirosPassos = "primeiros-passos-com-sql";
        public const string Ambiente = "ambiente";
        private const string PrimeirosPassosTitle = "Primeiros passos com SQL";
        private const string AmbienteTitle = "Ambiente: banco relacional + Docker";

        private static LessonRef L(string module, string slug, string title) => new(SqlPath, module, slug, title);

        public static readonly LessonRef PrimeiroSelect = L(PrimeirosPassos, "seu-primeiro-select", "Seu primeiro SELECT");
        public static readonly LessonRef Where = L(PrimeirosPassos, "filtrando-com-where", "Filtrando com WHERE");
        public static readonly LessonRef OrderByLimit = L(PrimeirosPassos, "ordenando-e-limitando-com-order-by-e-limit", "Ordenando e limitando com ORDER BY e LIMIT");
        public static readonly LessonRef Join = L(PrimeirosPassos, "juntando-tabelas-com-join", "Juntando tabelas com JOIN");
        public static readonly LessonRef GroupBy = L(PrimeirosPassos, "resumindo-dados-com-group-by", "Resumindo dados com COUNT, SUM, AVG e GROUP BY");

        public static readonly LessonRef BancoRelacional = L(Ambiente, "o-que-e-banco-relacional", "O que é um banco relacional");
        public static readonly LessonRef InstalandoDocker = L(Ambiente, "instalando-docker-desktop", "Instalando o Docker Desktop");

        /// <summary>Módulo 2 (antigo módulo 1): só rascunhos por enquanto.</summary>
        private static ModuleProgressViewModel AmbienteEmBreve() => new(Ambiente, AmbienteTitle, 0, 0, 0,
        [
            new(BancoRelacional, LessonStatus.ComingSoon),
            new(InstalandoDocker, LessonStatus.ComingSoon)
        ]);

        public static DashboardViewModel FirstAccess() => new(SqlPath, "SQL & Dados", 0, 5, 0, PrimeiroSelect, true,
        [
            new(PrimeirosPassos, PrimeirosPassosTitle, 0, 5, 0,
            [
                new(PrimeiroSelect, LessonStatus.Available),
                new(Where, LessonStatus.Locked, PrimeiroSelect),
                new(OrderByLimit, LessonStatus.Locked, Where),
                new(Join, LessonStatus.Locked, OrderByLimit),
                new(GroupBy, LessonStatus.Locked, Join)
            ]),
            AmbienteEmBreve()
        ]);

        public static DashboardViewModel InProgress() => new(SqlPath, "SQL & Dados", 3, 5, 60, Join, false,
        [
            new(PrimeirosPassos, PrimeirosPassosTitle, 3, 5, 60,
            [
                new(PrimeiroSelect, LessonStatus.Completed),
                new(Where, LessonStatus.Completed),
                new(OrderByLimit, LessonStatus.Completed),
                new(Join, LessonStatus.InProgress),
                new(GroupBy, LessonStatus.Locked, Join)
            ]),
            AmbienteEmBreve()
        ]);

        public static DashboardViewModel AllDone() => new(SqlPath, "SQL & Dados", 5, 5, 100, null, false,
        [
            new(PrimeirosPassos, PrimeirosPassosTitle, 5, 5, 100,
            [
                new(PrimeiroSelect, LessonStatus.Completed),
                new(Where, LessonStatus.Completed),
                new(OrderByLimit, LessonStatus.Completed),
                new(Join, LessonStatus.Completed),
                new(GroupBy, LessonStatus.Completed)
            ]),
            AmbienteEmBreve()
        ]);

        public static DashboardViewModel Empty() => new(SqlPath, "SQL & Dados", 0, 0, 0, null, true,
        [
            new(PrimeirosPassos, PrimeirosPassosTitle, 0, 0, 0,
            [
                new(PrimeiroSelect, LessonStatus.ComingSoon)
            ])
        ]);
    }
}
