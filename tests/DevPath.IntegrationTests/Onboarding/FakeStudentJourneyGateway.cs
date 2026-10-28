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
            DifficultyLevel.Beginner, 40, 20),
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

    /// <summary>Dashboards de exemplo (trilha SQL & Dados) para cada estado da US-11.3.</summary>
    public static class Samples
    {
        private static LessonRef L(string module, string slug, string title) => new(SqlPath, module, slug, title);

        public static readonly LessonRef PrimeiroSelect = L("primeiros-passos", "seu-primeiro-select", "Seu primeiro SELECT");
        public static readonly LessonRef Where = L("primeiros-passos", "filtrando-com-where", "Filtrando com WHERE");
        public static readonly LessonRef OrderBy = L("primeiros-passos", "ordenando-resultados", "Ordenando resultados");
        public static readonly LessonRef Limit = L("primeiros-passos", "limitando-resultados", "Limitando resultados");
        public static readonly LessonRef Join = L("consultas", "joins", "Juntando tabelas com JOIN");
        public static readonly LessonRef GroupBy = L("consultas", "agregacoes-group-by-having", "Agregações com GROUP BY");

        public static DashboardViewModel FirstAccess() => new(SqlPath, "SQL & Dados", 0, 20, 0, PrimeiroSelect, true,
        [
            new("primeiros-passos", "Primeiros passos com SQL", 0, 3, 0,
            [
                new(PrimeiroSelect, LessonStatus.Available),
                new(Where, LessonStatus.Locked, PrimeiroSelect),
                new(OrderBy, LessonStatus.Locked, Where),
                new(Limit, LessonStatus.ComingSoon)
            ]),
            new("consultas", "Consultas", 0, 17, 0,
            [
                new(Join, LessonStatus.Locked, OrderBy),
                new(GroupBy, LessonStatus.Locked, Join)
            ])
        ]);

        public static DashboardViewModel InProgress() => new(SqlPath, "SQL & Dados", 3, 20, 15, Join, false,
        [
            new("primeiros-passos", "Primeiros passos com SQL", 3, 3, 100,
            [
                new(PrimeiroSelect, LessonStatus.Completed),
                new(Where, LessonStatus.Completed),
                new(OrderBy, LessonStatus.Completed),
                new(Limit, LessonStatus.ComingSoon)
            ]),
            new("consultas", "Consultas", 0, 17, 0,
            [
                new(Join, LessonStatus.InProgress),
                new(GroupBy, LessonStatus.Locked, Join)
            ]),
            new("ambiente", "Ambiente: banco relacional + Docker", 0, 0, 0,
            [
                new(L("ambiente", "instalando-docker-desktop", "Instalando o Docker Desktop"), LessonStatus.ComingSoon)
            ])
        ]);

        public static DashboardViewModel AllDone() => new(SqlPath, "SQL & Dados", 3, 3, 100, null, false,
        [
            new("primeiros-passos", "Primeiros passos com SQL", 3, 3, 100,
            [
                new(PrimeiroSelect, LessonStatus.Completed),
                new(Where, LessonStatus.Completed),
                new(OrderBy, LessonStatus.Completed),
                new(Limit, LessonStatus.ComingSoon)
            ])
        ]);

        public static DashboardViewModel Empty() => new(SqlPath, "SQL & Dados", 0, 0, 0, null, true,
        [
            new("primeiros-passos", "Primeiros passos com SQL", 0, 0, 0,
            [
                new(PrimeiroSelect, LessonStatus.ComingSoon)
            ])
        ]);
    }
}
