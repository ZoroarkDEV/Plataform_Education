using System.Diagnostics.CodeAnalysis;
using DevPath.Domain.Common;

namespace DevPath.Application.Journey;

/// <summary>Uma trilha ativa na tela "Escolha sua trilha" (US-11.2).</summary>
/// <param name="AvailableLessons">
/// Aulas disponíveis: publicadas, de módulos publicados e que <b>não</b> são rascunho (R4).
/// 0 = nenhuma aula abre ainda ("Aulas em breve").
/// </param>
/// <param name="TotalLessons">
/// Todas as aulas publicadas de módulos publicados, <b>com</b> os rascunhos. Com <paramref name="AvailableLessons"/> = 0,
/// separa a trilha só com rascunhos (TotalLessons &gt; 0, "Aulas em breve") da trilha sem aula nenhuma (TotalLessons = 0).
/// Nos dois casos a trilha não pode ser escolhida: a matrícula dá <see cref="EnrollmentResultKind.NoLessons"/>.
/// </param>
public sealed record PathSummaryDto(
    string Slug,
    string Title,
    string Description,
    DifficultyLevel Level,
    int EstimatedHours,
    int AvailableLessons,
    int TotalLessons);

/// <summary>Resultado de <see cref="IStudentJourneyService.EnrollAsync"/>.</summary>
public enum EnrollmentResultKind
{
    /// <summary>Matrícula criada agora. <see cref="EnrollmentResult.PathTitle"/> = título da trilha escolhida.</summary>
    Enrolled,

    /// <summary>
    /// O aluno já tem uma trilha (a mesma ou outra). Nada muda: uma trilha por vez (SPEC §5.1) e
    /// trocar de trilha está fora do escopo da #11. Também é a resposta do 2º POST de um duplo clique.
    /// <see cref="EnrollmentResult.PathTitle"/> = título da trilha <b>atual</b> do aluno, mesmo que o slug pedido seja outro.
    /// Tem precedência sobre <see cref="PathNotFound"/> e <see cref="NoLessons"/>.
    /// </summary>
    AlreadyEnrolled,

    /// <summary>Slug inexistente ou trilha desativada.</summary>
    PathNotFound,

    /// <summary>
    /// A trilha existe e está ativa, mas não tem nenhuma aula <b>disponível</b> (publicada, de módulo publicado e não
    /// rascunho; o mesmo critério de <see cref="PathSummaryDto.AvailableLessons"/>). Inclui a trilha só com rascunhos
    /// (<see cref="PathSummaryDto.TotalLessons"/> &gt; 0 e AvailableLessons = 0), que fica "Em breve" (R4).
    /// </summary>
    NoLessons,

    /// <summary>Não existe usuário com esse Id (ex.: conta apagada com o cookie ainda válido). Verificado primeiro.</summary>
    UserNotFound
}

/// <summary>Resultado da matrícula.</summary>
/// <param name="Kind">O que aconteceu.</param>
/// <param name="PathTitle">
/// Sempre preenchido em <see cref="EnrollmentResultKind.Enrolled"/> (trilha escolhida) e
/// <see cref="EnrollmentResultKind.AlreadyEnrolled"/> (trilha atual); null nos demais. Veja <see cref="HasPath"/>.
/// </param>
public sealed record EnrollmentResult(EnrollmentResultKind Kind, string? PathTitle = null)
{
    /// <summary>true em Enrolled e AlreadyEnrolled; nesses casos o compilador sabe que <see cref="PathTitle"/> não é null.</summary>
    [MemberNotNullWhen(true, nameof(PathTitle))]
    public bool HasPath => Kind is EnrollmentResultKind.Enrolled or EnrollmentResultKind.AlreadyEnrolled && PathTitle is not null;

    public static EnrollmentResult Enrolled(string pathTitle) => new(EnrollmentResultKind.Enrolled, pathTitle);
    public static EnrollmentResult AlreadyEnrolled(string currentPathTitle) => new(EnrollmentResultKind.AlreadyEnrolled, currentPathTitle);
    public static EnrollmentResult PathNotFound() => new(EnrollmentResultKind.PathNotFound);
    public static EnrollmentResult NoLessons() => new(EnrollmentResultKind.NoLessons);
    public static EnrollmentResult UserNotFound() => new(EnrollmentResultKind.UserNotFound);
}

/// <summary>Endereço de uma aula: <c>/trilhas/{PathSlug}/{ModuleSlug}/{LessonSlug}</c>.</summary>
public sealed record LessonRefDto(string PathSlug, string ModuleSlug, string LessonSlug, string Title);

/// <summary>Status de uma aula no roadmap, calculado no backend (R1, R4, R7).</summary>
public enum JourneyLessonStatus
{
    Completed,
    InProgress,
    Available,
    Locked,
    /// <summary>Rascunho (R4): "Em breve", não abre, não conta no % e não bloqueia ninguém.</summary>
    ComingSoon
}

/// <param name="UnlockRequirement">Só em <see cref="JourneyLessonStatus.Locked"/>: a aula que falta concluir (US-11.4).</param>
public sealed record DashboardLessonDto(LessonRefDto Lesson, JourneyLessonStatus Status, LessonRefDto? UnlockRequirement = null);

/// <param name="AvailableLessons">Aulas do módulo que não são rascunho (base do %).</param>
/// <param name="TotalLessons">Todas as aulas visíveis do módulo, rascunhos incluídos (para "Aula X de Y").</param>
public sealed record DashboardModuleDto(
    string Slug,
    string Title,
    int CompletedLessons,
    int AvailableLessons,
    int ProgressPercentage,
    IReadOnlyList<DashboardLessonDto> Lessons)
{
    public int TotalLessons => Lessons.Count;
}

/// <summary>Dashboard do aluno (US-11.3).</summary>
/// <param name="NextLesson">
/// Destino do "Continuar": a 1ª aula, na ordem da trilha, que está liberada e não concluída
/// (Em andamento ou Liberada). null = nada a fazer: tudo concluído ou trilha sem aulas disponíveis.
/// </param>
/// <param name="IsFirstAccess">O aluno não tem nenhum progresso nas aulas desta trilha ("Começar pela aula 1").</param>
public sealed record DashboardDto(
    string PathSlug,
    string PathTitle,
    int CompletedLessons,
    int AvailableLessons,
    int ProgressPercentage,
    LessonRefDto? NextLesson,
    bool IsFirstAccess,
    IReadOnlyList<DashboardModuleDto> Modules)
{
    /// <summary>Todas as aulas visíveis da trilha, rascunhos incluídos.</summary>
    public int TotalLessons => Modules.Sum(m => m.TotalLessons);
}
