using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.Web.Onboarding;

/// <summary>
/// Porta da Web para o onboarding e o dashboard (issue #11), no formato dos view models das telas.
/// </summary>
/// <remarks>
/// <para>
/// A regra de negócio (matrícula, desbloqueio R1, percentual R3, rascunhos R4, progresso R5) é do
/// Backend: <c>DevPath.Application.Journey.IStudentJourneyService</c>. A implementação desta porta é o
/// adapter <see cref="StudentJourneyGateway"/>, que só mapeia os DTOs e traduz os casos de erro
/// esperados (sem exceção para o controller).
/// </para>
/// <para>
/// Os controllers dependem desta porta (e não do serviço) para os testes de unidade e de tela poderem
/// trocar o serviço por um dublê; o dublê com dados de exemplo existe SÓ nos projetos de teste.
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
    /// Dashboard da trilha atual, já calculado. <c>null</c> quando o aluno ainda não escolheu trilha
    /// (ou o usuário não existe mais: o serviço devolve null nos dois casos e o dashboard manda para a escolha;
    /// a conta apagada é detectada na matrícula, como <see cref="EnrollmentStatus.UserNotFound"/>).
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
    public static EnrollmentOutcome AlreadyEnrolled(string? currentPathTitle = null) => new(EnrollmentStatus.AlreadyEnrolled, currentPathTitle);
    public static EnrollmentOutcome PathNotFound() => new(EnrollmentStatus.PathNotFound);
    public static EnrollmentOutcome NoLessons() => new(EnrollmentStatus.NoLessons);
    public static EnrollmentOutcome UserNotFound() => new(EnrollmentStatus.UserNotFound);
}

public enum EnrollmentStatus
{
    /// <summary>Matrícula criada agora (<see cref="EnrollmentOutcome.PathTitle"/> preenchido).</summary>
    Enrolled,
    /// <summary>
    /// O aluno já tem uma trilha (a mesma ou outra); trocar de trilha está fora do escopo do #11.
    /// <see cref="EnrollmentOutcome.PathTitle"/> = título da trilha atual do aluno.
    /// </summary>
    AlreadyEnrolled,
    /// <summary>Slug inexistente ou trilha inativa.</summary>
    PathNotFound,
    /// <summary>
    /// A trilha está ativa, mas não tem nenhuma aula disponível (inclusive quando só tem rascunhos).
    /// Não é serviço fora do ar.
    /// </summary>
    NoLessons,
    /// <summary>O usuário do cookie não existe mais no banco (conta apagada): a Web encerra a sessão.</summary>
    UserNotFound
}
