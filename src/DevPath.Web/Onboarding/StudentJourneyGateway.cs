using DevPath.Application.Journey;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Onboarding;

/// <summary>
/// Adapter da porta da Web (<see cref="IStudentJourneyGateway"/>) para o serviço do Backend
/// (<see cref="IStudentJourneyService"/>, registrado em <c>AddInfrastructure</c>).
/// </summary>
/// <remarks>
/// Só traduz: as regras (matrícula, R1, R3, R4) ficam no serviço. Os DTOs são mapeados campo a campo
/// e os enums com <c>switch</c> explícito (nunca cast), para que um membro novo ou reordenado no
/// Backend quebre os testes em vez de virar o status errado na tela.
/// </remarks>
public sealed class StudentJourneyGateway(IStudentJourneyService service, ILogger<StudentJourneyGateway> logger)
    : IStudentJourneyGateway
{
    public async Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken)
    {
        var paths = await service.GetActivePathsAsync(cancellationToken);
        return paths.Select(ToViewModel).ToList();
    }

    public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) =>
        service.HasCurrentPathAsync(userId, cancellationToken);

    public async Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken)
    {
        // O serviço não lança para os casos esperados (UserNotFound, AlreadyEnrolled, PathNotFound, NoLessons):
        // qualquer exceção aqui é falha de verdade e não é engolida.
        var result = await service.EnrollAsync(userId, pathSlug, cancellationToken);
        if (result.Kind == EnrollmentResultKind.UserNotFound)
        {
            // Conta apagada com o cookie ainda válido: o controller encerra a sessão.
            logger.LogWarning("Matrícula recusada: usuário {UserId} não existe.", userId);
        }

        return ToOutcome(result);
    }

    public async Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var dashboard = await service.GetDashboardAsync(userId, cancellationToken);
        return dashboard is null ? null : ToViewModel(dashboard);
    }

    public static PathOptionViewModel ToViewModel(PathSummaryDto dto) =>
        new(dto.Slug, dto.Title, dto.Description, dto.Level, dto.EstimatedHours, dto.AvailableLessons);

    public static EnrollmentOutcome ToOutcome(EnrollmentResult result) => result.Kind switch
    {
        EnrollmentResultKind.Enrolled => new EnrollmentOutcome(EnrollmentStatus.Enrolled, result.HasPath ? result.PathTitle : null),
        // PathTitle = título da trilha ATUAL do aluno (mesmo que o slug pedido seja outro).
        EnrollmentResultKind.AlreadyEnrolled => EnrollmentOutcome.AlreadyEnrolled(result.HasPath ? result.PathTitle : null),
        EnrollmentResultKind.PathNotFound => EnrollmentOutcome.PathNotFound(),
        // Trilha ativa sem aula disponível, inclusive só com rascunhos (R4). Não é serviço fora do ar.
        EnrollmentResultKind.NoLessons => EnrollmentOutcome.NoLessons(),
        EnrollmentResultKind.UserNotFound => EnrollmentOutcome.UserNotFound(),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Kind, "EnrollmentResultKind sem mapeamento na Web.")
    };

    public static LessonRef ToViewModel(LessonRefDto dto) =>
        new(dto.PathSlug, dto.ModuleSlug, dto.LessonSlug, dto.Title);

    public static LessonStatus ToViewModel(JourneyLessonStatus status) => status switch
    {
        JourneyLessonStatus.Completed => LessonStatus.Completed,
        JourneyLessonStatus.InProgress => LessonStatus.InProgress,
        JourneyLessonStatus.Available => LessonStatus.Available,
        JourneyLessonStatus.Locked => LessonStatus.Locked,
        JourneyLessonStatus.ComingSoon => LessonStatus.ComingSoon,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "JourneyLessonStatus sem mapeamento na Web.")
    };

    public static LessonItemViewModel ToViewModel(DashboardLessonDto dto) =>
        new(ToViewModel(dto.Lesson),
            ToViewModel(dto.Status),
            dto.UnlockRequirement is null ? null : ToViewModel(dto.UnlockRequirement));

    public static ModuleProgressViewModel ToViewModel(DashboardModuleDto dto) =>
        new(dto.Slug, dto.Title, dto.CompletedLessons, dto.AvailableLessons, dto.ProgressPercentage,
            dto.Lessons.Select(ToViewModel).ToList());

    public static DashboardViewModel ToViewModel(DashboardDto dto) =>
        new(dto.PathSlug, dto.PathTitle, dto.CompletedLessons, dto.AvailableLessons, dto.ProgressPercentage,
            dto.NextLesson is null ? null : ToViewModel(dto.NextLesson),
            dto.IsFirstAccess,
            dto.Modules.Select(ToViewModel).ToList());
}
