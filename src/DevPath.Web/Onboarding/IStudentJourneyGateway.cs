using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Onboarding;

/// <summary>
/// PORTA PROVISÓRIA da Web para o onboarding e o dashboard (issue #11).
/// </summary>
/// <remarks>
/// <para>
/// A regra de negócio (matrícula, desbloqueio R1, percentual R3, rascunhos R4, progresso R5) é do
/// Backend e ainda não existe na <c>dev</c>. Esta interface só descreve o que as telas precisam ler,
/// já no formato dos view models, para não duplicar nada que o Backend vá criar em
/// <c>DevPath.Application</c>.
/// </para>
/// <para>
/// Quando o serviço real existir, a troca é: um adapter fino na Web que implementa esta interface
/// chamando o serviço da Application e mapeando os DTOs (ou as views passam a usar os DTOs e esta
/// interface é apagada). Contrato proposto: <c>docs/frontend.md</c>, seção "Onboarding e dashboard".
/// </para>
/// <para>
/// Implementações: <see cref="PendingStudentJourneyGateway"/> (padrão, sem dados: as telas mostram os
/// estados vazios, nunca erro 500) e um dublê com dados de exemplo que existe SÓ nos projetos de teste.
/// </para>
/// </remarks>
public interface IStudentJourneyGateway
{
    /// <summary>Trilhas ativas (<c>CareerPath.IsActive</c>), na ordem de exibição. Lista vazia = estado vazio.</summary>
    Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken);

    /// <summary>O aluno já escolheu uma trilha (<c>ApplicationUser.CurrentPathId</c> preenchido)?</summary>
    Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Matricula o aluno na trilha (cria <c>UserPathEnrollment</c>, define <c>CurrentPathId</c> e
    /// <c>CurrentModuleId</c>). Uma trilha por vez (SPEC §5.1): se já tiver uma, devolve
    /// <see cref="EnrollmentStatus.AlreadyEnrolled"/> e não troca.
    /// </summary>
    Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken);

    /// <summary>
    /// Dashboard da trilha atual, já calculado. <c>null</c> quando o aluno ainda não escolheu trilha.
    /// Módulos na ordem da trilha (<c>Module.Order</c>) e aulas na ordem do módulo (<c>Lesson.Order</c>):
    /// a tela numera "Módulo 1, 2..." pela posição na lista (no SQL &amp; Dados, "Primeiros passos com SQL"
    /// é o módulo 1, issue #22).
    /// </summary>
    Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Resultado de <see cref="IStudentJourneyGateway.EnrollAsync"/> (sem exceção para erro esperado).</summary>
public sealed record EnrollmentOutcome(EnrollmentStatus Status, string? PathTitle = null)
{
    public static EnrollmentOutcome Enrolled(string pathTitle) => new(EnrollmentStatus.Enrolled, pathTitle);
    public static EnrollmentOutcome AlreadyEnrolled() => new(EnrollmentStatus.AlreadyEnrolled);
    public static EnrollmentOutcome PathNotFound() => new(EnrollmentStatus.PathNotFound);
    public static EnrollmentOutcome Unavailable() => new(EnrollmentStatus.Unavailable);
}

public enum EnrollmentStatus
{
    Enrolled,
    /// <summary>O aluno já tem uma trilha; trocar de trilha está fora do escopo do #11.</summary>
    AlreadyEnrolled,
    /// <summary>Slug inexistente ou trilha inativa.</summary>
    PathNotFound,
    /// <summary>Serviço ainda não disponível (implementação pendente do Backend).</summary>
    Unavailable
}
