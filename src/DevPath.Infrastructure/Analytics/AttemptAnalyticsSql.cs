namespace DevPath.Infrastructure.Analytics;

/// <summary>
/// As consultas do relatório de tentativas (#43). Copiadas, iguais, em <c>docs/analytics/tentativas.md</c>
/// (um teste confere que o documento e o código não se separam).
/// </summary>
public static class AttemptAnalyticsSql
{
    /// <summary>Tentativas até o primeiro acerto, por aula: mediana, média e distribuição.</summary>
    public const string AttemptsUntilPass = """
        WITH visible_lessons AS (
            SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
                   m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
            FROM "Lessons" l
            JOIN "Modules" m ON m."Id" = l."ModuleId"
            JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
            WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
        ),
        exercises AS (
            SELECT e."Id" AS exercise_id, e."LessonId" AS lesson_id
            FROM "Exercises" e
            JOIN visible_lessons vl ON vl.lesson_id = e."LessonId"
            WHERE e."IsPublished"
        ),
        first_pass AS (
            SELECT s."UserId" AS user_id, x.lesson_id, x.exercise_id, MIN(s."SubmittedAt") AS passed_at
            FROM "CodeSubmissions" s
            JOIN exercises x ON x.exercise_id = s."ExerciseId"
            WHERE s."Result" = 'Pass'
            GROUP BY s."UserId", x.lesson_id, x.exercise_id
        ),
        lesson_pass AS (
            SELECT fp.user_id, fp.lesson_id, MAX(fp.passed_at) AS passed_at
            FROM first_pass fp
            GROUP BY fp.user_id, fp.lesson_id
            HAVING COUNT(*) = (SELECT COUNT(*) FROM exercises x WHERE x.lesson_id = fp.lesson_id)
        ),
        attempts AS (
            SELECT lp.lesson_id, lp.user_id, COUNT(*) AS attempts
            FROM lesson_pass lp
            JOIN exercises x ON x.lesson_id = lp.lesson_id
            JOIN "CodeSubmissions" s ON s."ExerciseId" = x.exercise_id AND s."UserId" = lp.user_id
                                    AND s."SubmittedAt" <= lp.passed_at
            GROUP BY lp.lesson_id, lp.user_id
        ),
        tried AS (
            SELECT x.lesson_id, COUNT(DISTINCT s."UserId") AS students
            FROM exercises x
            JOIN "CodeSubmissions" s ON s."ExerciseId" = x.exercise_id
            GROUP BY x.lesson_id
        )
        SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
               vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
               COALESCE(MAX(t.students), 0)::int AS "StudentsTried",
               COUNT(a.user_id)::int AS "StudentsPassed",
               percentile_cont(0.5) WITHIN GROUP (ORDER BY a.attempts) AS "MedianAttempts",
               AVG(a.attempts)::float8 AS "AverageAttempts",
               COUNT(*) FILTER (WHERE a.attempts = 1)::int AS "One",
               COUNT(*) FILTER (WHERE a.attempts = 2)::int AS "Two",
               COUNT(*) FILTER (WHERE a.attempts = 3)::int AS "Three",
               COUNT(*) FILTER (WHERE a.attempts = 4)::int AS "Four",
               COUNT(*) FILTER (WHERE a.attempts = 5)::int AS "Five",
               COUNT(*) FILTER (WHERE a.attempts BETWEEN 6 AND 10)::int AS "SixToTen",
               COUNT(*) FILTER (WHERE a.attempts >= 11)::int AS "ElevenOrMore"
        FROM visible_lessons vl
        LEFT JOIN tried t ON t.lesson_id = vl.lesson_id
        LEFT JOIN attempts a ON a.lesson_id = vl.lesson_id
        WHERE EXISTS (SELECT 1 FROM exercises x WHERE x.lesson_id = vl.lesson_id)
        GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
        ORDER BY vl.path_order, vl.path_title, vl.module_order, vl.lesson_order
        """;

    /// <summary>Alunos que usaram o "Seguir sem pontos", por aula (a que mais usou primeiro).</summary>
    public const string Skips = """
        WITH visible_lessons AS (
            SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
                   m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
            FROM "Lessons" l
            JOIN "Modules" m ON m."Id" = l."ModuleId"
            JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
            WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
        )
        SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
               vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
               COUNT(*)::int AS "StudentsSkipped",
               COUNT(*) FILTER (WHERE up."CompletionKind" = 'SkippedAfterFailures')::int AS "StillSkipped",
               COUNT(*) FILTER (WHERE up."CompletionKind" = 'ExercisePassed')::int AS "PassedAfterSkip"
        FROM visible_lessons vl
        JOIN "UserProgress" up ON up."LessonId" = vl.lesson_id
        WHERE up."SkippedAt" IS NOT NULL
        GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
        ORDER BY "StudentsSkipped" DESC, vl.path_order, vl.path_title, vl.module_order, vl.lesson_order
        """;

    /// <summary>Taxa de falha por aula (tentativas que não passaram / tentativas), a maior primeiro.</summary>
    public const string FailureRates = """
        WITH visible_lessons AS (
            SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
                   m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
            FROM "Lessons" l
            JOIN "Modules" m ON m."Id" = l."ModuleId"
            JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
            WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
        )
        SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
               vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
               COUNT(*)::int AS "Attempts",
               COUNT(DISTINCT s."UserId")::int AS "Students",
               COUNT(*) FILTER (WHERE s."Result" = 'Pass')::int AS "Passed",
               COUNT(*) FILTER (WHERE s."Outcome" = 'Fail')::int AS "Wrong",
               COUNT(*) FILTER (WHERE s."Outcome" IN ('Error', 'Timeout'))::int AS "Errors",
               COUNT(*) FILTER (WHERE s."Result" = 'Fail' AND s."Outcome" IS NULL)::int AS "FailedWithoutDetail",
               ROUND(100.0 * COUNT(*) FILTER (WHERE s."Result" = 'Fail') / COUNT(*), 1) AS "FailureRatePercent"
        FROM visible_lessons vl
        JOIN "Exercises" e ON e."LessonId" = vl.lesson_id AND e."IsPublished"
        JOIN "CodeSubmissions" s ON s."ExerciseId" = e."Id"
        GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
        ORDER BY "FailureRatePercent" DESC, vl.path_order, vl.path_title, vl.module_order, vl.lesson_order
        """;
}
