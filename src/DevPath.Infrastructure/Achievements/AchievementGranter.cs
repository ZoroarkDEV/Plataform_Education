using DevPath.Application.Achievements;
using DevPath.Domain.Achievements;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DevPath.Infrastructure.Achievements;

/// <summary>
/// Concessão do selo "Primeiro SELECT" (#45, US-BADGE.1), usada pelo Worker (no <c>pass</c>) e pela Web (backfill do
/// dashboard). Tudo num único comando SQL com <c>INSERT ... ON CONFLICT DO NOTHING</c> na PK (UserId, AchievementId):
/// dois acertos simultâneos nunca gravam duas linhas e só quem de fato inseriu recebe o aviso.
/// </summary>
/// <remarks>
/// <para>Quem "já tinha direito" antes deste acerto (<see cref="QualifiedSinceSql"/>):</para>
/// <list type="bullet">
/// <item>uma <c>CodeSubmission</c> com <c>Result</c> ou <c>Outcome</c> = <c>Pass</c>, de qualquer exercício (sem contar
/// a tentativa atual); ou</item>
/// <item>a aula <c>seu-primeiro-select</c> concluída com <c>ExercisePassed</c> (dados de antes do #45).</item>
/// </list>
/// <para>
/// <c>SkippedAfterFailures</c> e <c>AttemptsExhausted</c> não contam como acerto. Quem já tinha direito ganha o selo em
/// silêncio, com <c>EarnedAt</c> = a data do primeiro acerto. O selo não dá pontos (não mexe em <c>UserProgress</c>).
/// </para>
/// </remarks>
public sealed class AchievementGranter(DevPathDbContext db)
{
    /// <summary>
    /// Desde quando o aluno já tem direito ao selo, sem contar a tentativa <c>@exclude</c>. NULL = ainda não tem.
    /// <c>LEAST</c> do PostgreSQL ignora NULL.
    /// </summary>
    private const string QualifiedSinceSql = """
        LEAST(
            (SELECT min(s."SubmittedAt") FROM "CodeSubmissions" s
             WHERE s."UserId" = @user AND s."Id" <> @exclude AND (s."Result" = 'Pass' OR s."Outcome" = 'Pass')),
            (SELECT min(COALESCE(p."CompletedAt", p."LastAccessedAt")) FROM "UserProgress" p
             JOIN "Lessons" l ON l."Id" = p."LessonId"
             WHERE p."UserId" = @user AND l."Slug" = @lessonSlug
               AND p."CompletionKind" = 'ExercisePassed' AND p."Status" IN ('Completed', 'Mastered')))
        """;

    private const string GrantSql = $"""
        WITH q AS (SELECT {QualifiedSinceSql} AS at),
        ins AS (
            INSERT INTO "UserAchievements" ("UserId", "AchievementId", "EarnedAt")
            SELECT @user, a."Id", COALESCE(q.at, @now)
            FROM "Achievements" a CROSS JOIN q
            WHERE a."Id" = @achievement AND (@isPass OR q.at IS NOT NULL)
            ON CONFLICT ("UserId", "AchievementId") DO NOTHING
            RETURNING "AchievementId", "EarnedAt")
        SELECT a."Title" AS "Title", a."Description" AS "Description", ins."EarnedAt" AS "EarnedAt",
               (q.at IS NOT NULL) AS "Silent"
        FROM ins JOIN "Achievements" a ON a."Id" = ins."AchievementId" CROSS JOIN q
        """;

    private sealed class GrantRow
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime EarnedAt { get; set; }
        public bool Silent { get; set; }
    }

    /// <summary>
    /// Chamado pelo Worker DEPOIS de gravar uma tentativa <c>pass</c>, NA MESMA transação (precisa de transação aberta).
    /// Devolve o selo só quando ESTE acerto acabou de inserir a linha e é o primeiro acerto da vida do aluno; null se já
    /// tinha o selo, se já tinha direito (backfill silencioso) ou se o selo não existe no banco.
    /// </summary>
    public async Task<EarnedAchievementDto?> GrantOnPassAsync(Guid userId, Guid passSubmissionId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A concessão no acerto precisa da transação da tentativa.");
        }

        var row = await GrantAsync(userId, passSubmissionId, isPass: true, ct);
        return row is null || row.Silent ? null : ToDto(row);
    }

    /// <summary>Backfill silencioso: concede se o aluno já tem direito e ainda não tem o selo. true = inseriu agora.</summary>
    public async Task<bool> BackfillAsync(Guid userId, CancellationToken ct = default) =>
        await GrantAsync(userId, Guid.Empty, isPass: false, ct) is not null;

    private async Task<GrantRow?> GrantAsync(Guid userId, Guid exclude, bool isPass, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<GrantRow>(GrantSql,
                new NpgsqlParameter("user", NpgsqlDbType.Uuid) { Value = userId },
                new NpgsqlParameter("exclude", NpgsqlDbType.Uuid) { Value = exclude },
                new NpgsqlParameter("lessonSlug", NpgsqlDbType.Text) { Value = AchievementIds.PrimeiroSelectBackfillLessonSlug },
                new NpgsqlParameter("achievement", NpgsqlDbType.Uuid) { Value = AchievementIds.PrimeiroSelect },
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = DateTime.UtcNow },
                new NpgsqlParameter("isPass", NpgsqlDbType.Boolean) { Value = isPass })
            .ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    internal static EarnedAchievementDto ToDto(string title, string? description, DateTime earnedAt) =>
        new(title, description ?? string.Empty, new DateTimeOffset(DateTime.SpecifyKind(earnedAt, DateTimeKind.Utc)));

    private static EarnedAchievementDto ToDto(GrantRow row) => ToDto(row.Title, row.Description, row.EarnedAt);
}
