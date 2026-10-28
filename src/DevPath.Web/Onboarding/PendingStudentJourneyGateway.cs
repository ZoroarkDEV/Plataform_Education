using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Onboarding;

/// <summary>
/// Implementação padrão enquanto o serviço do Backend não existe. NÃO tem dados de exemplo:
/// devolve "nada" e as telas mostram os estados vazios do PO ("Estamos preparando as trilhas.
/// Volte em breve."), sem erro 500 em nenhum ambiente.
/// </summary>
/// <remarks>
/// Registrada com <c>TryAdd</c> no Program.cs: quando o Backend registrar a implementação real
/// (em <c>AddInfrastructure</c>, antes), esta sai de cena sozinha.
/// </remarks>
public sealed class PendingStudentJourneyGateway(ILogger<PendingStudentJourneyGateway> logger) : IStudentJourneyGateway
{
    public Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("Onboarding: serviço de trilhas/matrícula ainda não implementado (issue #11); mostrando estado vazio.");
        return Task.FromResult<IReadOnlyList<PathOptionViewModel>>([]);
    }

    public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken) =>
        Task.FromResult(EnrollmentOutcome.Unavailable());

    public Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<DashboardViewModel?>(null);
}
