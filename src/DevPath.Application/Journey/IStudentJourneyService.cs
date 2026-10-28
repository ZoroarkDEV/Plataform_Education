namespace DevPath.Application.Journey;

/// <summary>Onboarding (escolha de trilha) e dashboard de progresso do aluno (issue #11, SPEC §6.1).</summary>
public interface IStudentJourneyService
{
    /// <summary>Trilhas ativas, na ordem do conteúdo. Lista vazia = "Estamos preparando as trilhas".</summary>
    Task<IReadOnlyList<PathSummaryDto>> GetActivePathsAsync(CancellationToken ct = default);

    /// <summary>true se o aluno já escolheu uma trilha (vai direto ao dashboard).</summary>
    Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Matricula o aluno na trilha: cria a <c>UserPathEnrollment</c> e preenche <c>CurrentPathId</c>
    /// e <c>CurrentModuleId</c>. Idempotente e seguro contra POST duplo (o 2º recebe AlreadyEnrolled).
    /// </summary>
    /// <exception cref="InvalidOperationException">O usuário não existe.</exception>
    Task<EnrollmentResult> EnrollAsync(Guid userId, string pathSlug, CancellationToken ct = default);

    /// <summary>Dashboard da trilha atual; null se o aluno ainda não escolheu trilha.</summary>
    Task<DashboardDto?> GetDashboardAsync(Guid userId, CancellationToken ct = default);
}
