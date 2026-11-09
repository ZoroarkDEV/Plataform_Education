using DevPath.Application.Analytics;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Analytics;

/// <summary>
/// Relatório de tentativas (#43) direto do registro que já existe: <c>CodeSubmissions</c> (cada Executar que rodou)
/// e <c>UserProgress.SkippedAt</c>. Três consultas agregadas no banco, uma por tabela da página: sem N+1.
/// </summary>
public sealed class AttemptAnalyticsService(DevPathDbContext db) : IAttemptAnalyticsService
{
    public async Task<AttemptsReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var attempts = await db.Database.SqlQueryRaw<AttemptsRow>(AttemptAnalyticsSql.AttemptsUntilPass).ToListAsync(cancellationToken);
        var skips = await db.Database.SqlQueryRaw<SkipRow>(AttemptAnalyticsSql.Skips).ToListAsync(cancellationToken);
        var failures = await db.Database.SqlQueryRaw<FailureRow>(AttemptAnalyticsSql.FailureRates).ToListAsync(cancellationToken);

        return new AttemptsReport(
            attempts.Select(r => new AttemptsUntilPassStats(r.Lesson(), r.StudentsTried, r.StudentsPassed, r.MedianAttempts,
                r.AverageAttempts, new AttemptsDistribution(r.One, r.Two, r.Three, r.Four, r.Five, r.SixToTen, r.ElevenOrMore)))
                .ToList(),
            skips.Select(r => new SkipStats(r.Lesson(), r.StudentsSkipped, r.StillSkipped, r.PassedAfterSkip)).ToList(),
            failures.Select(r => new FailureRateStats(r.Lesson(), r.Attempts, r.Students, r.Passed, r.Wrong, r.Errors,
                r.FailedWithoutDetail, r.FailureRatePercent)).ToList());
    }

    private abstract class LessonRow
    {
        public Guid LessonId { get; set; }
        public string PathTitle { get; set; } = "";
        public int ModuleOrder { get; set; }
        public string ModuleTitle { get; set; } = "";
        public int LessonOrder { get; set; }
        public string LessonTitle { get; set; } = "";

        public AnalyticsLesson Lesson() => new(LessonId, PathTitle, ModuleOrder, ModuleTitle, LessonOrder, LessonTitle);
    }

    private sealed class AttemptsRow : LessonRow
    {
        public int StudentsTried { get; set; }
        public int StudentsPassed { get; set; }
        public double? MedianAttempts { get; set; }
        public double? AverageAttempts { get; set; }
        public int One { get; set; }
        public int Two { get; set; }
        public int Three { get; set; }
        public int Four { get; set; }
        public int Five { get; set; }
        public int SixToTen { get; set; }
        public int ElevenOrMore { get; set; }
    }

    private sealed class SkipRow : LessonRow
    {
        public int StudentsSkipped { get; set; }
        public int StillSkipped { get; set; }
        public int PassedAfterSkip { get; set; }
    }

    private sealed class FailureRow : LessonRow
    {
        public int Attempts { get; set; }
        public int Students { get; set; }
        public int Passed { get; set; }
        public int Wrong { get; set; }
        public int Errors { get; set; }
        public int FailedWithoutDetail { get; set; }
        public decimal FailureRatePercent { get; set; }
    }
}
