namespace DevPath.Application.Journey;

/// <summary>Onboarding (escolha de trilha) e dashboard de progresso do aluno (issue #11, SPEC §6.1).</summary>
public interface IStudentJourneyService
{
    /// <summary>Trilhas ativas, na ordem do conteúdo. Lista vazia = "Estamos preparando as trilhas".</summary>
    Task<IReadOnlyList<PathSummaryDto>> GetActivePathsAsync(CancellationToken ct = default);

    /// <summary>true se o aluno já escolheu uma trilha (vai direto ao dashboard). Usuário inexistente → false (não lança).</summary>
    Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Matricula o aluno na trilha: cria a <c>UserPathEnrollment</c> e preenche <c>CurrentPathId</c>
    /// e <c>CurrentModuleId</c>. Idempotente e seguro contra POST duplo (o 2º recebe AlreadyEnrolled).
    /// </summary>
    /// <remarks>
    /// Ordem das verificações: <see cref="EnrollmentResultKind.UserNotFound"/> &gt; <see cref="EnrollmentResultKind.AlreadyEnrolled"/>
    /// &gt; <see cref="EnrollmentResultKind.PathNotFound"/> &gt; <see cref="EnrollmentResultKind.NoLessons"/> &gt;
    /// <see cref="EnrollmentResultKind.Enrolled"/>. Não lança para nenhum desses casos.
    /// <see cref="EnrollmentResultKind.NoLessons"/> = a trilha não tem aula disponível (publicada, de módulo publicado e
    /// não rascunho), inclusive quando só tem rascunhos.
    /// A precedência vale para chamadas sequenciais: numa corrida, quem perde pode receber PathNotFound/NoLessons
    /// em vez de AlreadyEnrolled se checou antes do commit de quem ganhou. O banco não muda nesse caso.
    /// </remarks>
    Task<EnrollmentResult> EnrollAsync(Guid userId, string pathSlug, CancellationToken ct = default);

    /// <summary>Dashboard da trilha atual; null se o aluno ainda não escolheu trilha ou se o usuário não existe (não lança).</summary>
    Task<DashboardDto?> GetDashboardAsync(Guid userId, CancellationToken ct = default);
}
