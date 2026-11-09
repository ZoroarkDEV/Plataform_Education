namespace DevPath.Application.Analytics;

/// <summary>Papel (role do ASP.NET Identity) e policy da área administrativa (#43).</summary>
/// <remarks>Como dar o papel a alguém: <c>docs/analytics/tentativas.md</c>.</remarks>
public static class AdminAccess
{
    public const string Role = "Admin";
    public const string Policy = "Admin";
}

/// <summary>Onde a aula fica (trilha, módulo, aula), na ordem da trilha.</summary>
public sealed record AnalyticsLesson(
    Guid LessonId,
    string PathTitle,
    int ModuleOrder,
    string ModuleTitle,
    int LessonOrder,
    string LessonTitle);

/// <summary>
/// "Quantas tentativas até acertar a aula?" Conta, por aluno, as tentativas gravadas nos exercícios da aula até o
/// primeiro acerto (inclusive). Quem ainda não acertou fica fora da mediana/média (aparece em <see cref="StudentsTried"/>).
/// </summary>
public sealed record AttemptsUntilPassStats(
    AnalyticsLesson Lesson,
    int StudentsTried,
    int StudentsPassed,
    double? MedianAttempts,
    double? AverageAttempts,
    AttemptsDistribution Distribution);

/// <summary>Quantos alunos acertaram com 1, 2, 3, 4, 5, 6 a 10 e 11 ou mais tentativas.</summary>
public sealed record AttemptsDistribution(int One, int Two, int Three, int Four, int Five, int SixToTen, int ElevenOrMore);

/// <summary>"Em qual aula mais gente usa Seguir sem pontos?"</summary>
/// <param name="StudentsSkipped">Alunos que usaram o "Seguir sem pontos" na aula (alguma vez).</param>
/// <param name="StillSkipped">Destes, quantos continuam com a aula concluída sem pontos.</param>
/// <param name="PassedAfterSkip">Destes, quantos acertaram depois (conclusão promovida para os pontos cheios).</param>
public sealed record SkipStats(AnalyticsLesson Lesson, int StudentsSkipped, int StillSkipped, int PassedAfterSkip);

/// <summary>Taxa de falha por aula: tentativas que não passaram / tentativas.</summary>
/// <param name="Wrong">Rodou e deu resultado diferente (errado).</param>
/// <param name="Errors">Erro do código ou timeout.</param>
/// <param name="FailedWithoutDetail">Não passou, gravada antes do #43 (sem separar errado de erro).</param>
/// <param name="FailureRatePercent">0 a 100, uma casa decimal.</param>
public sealed record FailureRateStats(
    AnalyticsLesson Lesson,
    int Attempts,
    int Students,
    int Passed,
    int Wrong,
    int Errors,
    int FailedWithoutDetail,
    decimal FailureRatePercent);

public sealed record AttemptsReport(
    IReadOnlyList<AttemptsUntilPassStats> AttemptsUntilPass,
    IReadOnlyList<SkipStats> Skips,
    IReadOnlyList<FailureRateStats> FailureRates);

/// <summary>
/// Relatório de tentativas para o dono (#43, página /admin/tentativas). Só aulas visíveis aos alunos: trilha ativa,
/// módulo publicado, aula publicada e fora de rascunho; só exercícios publicados.
/// Número fixo de consultas (uma por tabela), sem N+1. As mesmas SQL estão em <c>docs/analytics/tentativas.md</c>.
/// </summary>
public interface IAttemptAnalyticsService
{
    Task<AttemptsReport> GetReportAsync(CancellationToken cancellationToken = default);
}
