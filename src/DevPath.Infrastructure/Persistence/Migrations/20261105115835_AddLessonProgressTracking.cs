using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonProgressTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProgress_UserId_LessonId",
                table: "UserProgress");

            migrationBuilder.AddColumn<string>(
                name: "CompletionKind",
                table: "UserProgress",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailuresBeforeSkip",
                table: "Exercises",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Datasets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Antes do índice único (aluno, aula): consolida duplicatas em uma linha
            // (melhor Status; MAX(Score); MIN(CompletedAt); MAX(LastAccessedAt); SUM(tempo);
            // CompletionKind do vencedor ou de outra linha do grupo se o vencedor não tiver).
            // Depois apaga as perdedoras. Linhas só de ExerciseId (LessonId null) não entram.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", "UserId", "LessonId",
                           ROW_NUMBER() OVER (
                               PARTITION BY "UserId", "LessonId"
                               ORDER BY CASE "Status"
                                            WHEN 'Mastered' THEN 3
                                            WHEN 'Completed' THEN 2
                                            WHEN 'InProgress' THEN 1
                                            ELSE 0
                                        END DESC,
                                        "Score" DESC NULLS LAST,
                                        "LastAccessedAt" DESC,
                                        "Id") AS rn
                    FROM "UserProgress"
                    WHERE "LessonId" IS NOT NULL
                ),
                agg AS (
                    SELECT "UserId", "LessonId",
                           MAX("Score") AS max_score,
                           MIN("CompletedAt") AS min_completed_at,
                           MAX("LastAccessedAt") AS max_last_accessed,
                           SUM("TimeSpentSeconds")::int AS sum_time,
                           (ARRAY_AGG("CompletionKind" ORDER BY
                               CASE WHEN "CompletionKind" IS NULL THEN 1 ELSE 0 END,
                               CASE "Status"
                                    WHEN 'Mastered' THEN 3
                                    WHEN 'Completed' THEN 2
                                    WHEN 'InProgress' THEN 1
                                    ELSE 0
                               END DESC,
                               "Score" DESC NULLS LAST,
                               "LastAccessedAt" DESC,
                               "Id"))[1] AS best_kind
                    FROM "UserProgress"
                    WHERE "LessonId" IS NOT NULL
                    GROUP BY "UserId", "LessonId"
                    HAVING COUNT(*) > 1
                )
                UPDATE "UserProgress" up
                SET "Score" = agg.max_score,
                    "CompletedAt" = agg.min_completed_at,
                    "LastAccessedAt" = agg.max_last_accessed,
                    "TimeSpentSeconds" = agg.sum_time,
                    "CompletionKind" = COALESCE(up."CompletionKind", agg.best_kind)
                FROM ranked, agg
                WHERE up."Id" = ranked."Id"
                  AND ranked.rn = 1
                  AND ranked."UserId" = agg."UserId"
                  AND ranked."LessonId" = agg."LessonId";

                DELETE FROM "UserProgress" up
                USING (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "UserId", "LessonId"
                        ORDER BY CASE "Status"
                                     WHEN 'Mastered' THEN 3
                                     WHEN 'Completed' THEN 2
                                     WHEN 'InProgress' THEN 1
                                     ELSE 0
                                 END DESC,
                                 "Score" DESC NULLS LAST,
                                 "LastAccessedAt" DESC,
                                 "Id") AS rn
                    FROM "UserProgress"
                    WHERE "LessonId" IS NOT NULL
                ) ranked
                WHERE up."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_UserId_LessonId",
                table: "UserProgress",
                columns: new[] { "UserId", "LessonId" },
                unique: true,
                filter: "\"LessonId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserProgress_CompletionKind",
                table: "UserProgress",
                sql: "\"CompletionKind\" IS NULL OR \"Status\" IN ('Completed', 'Mastered')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exercises_FailuresBeforeSkip_Positive",
                table: "Exercises",
                sql: "\"FailuresBeforeSkip\" IS NULL OR \"FailuresBeforeSkip\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProgress_UserId_LessonId",
                table: "UserProgress");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserProgress_CompletionKind",
                table: "UserProgress");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Exercises_FailuresBeforeSkip_Positive",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "CompletionKind",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "FailuresBeforeSkip",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Datasets");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_UserId_LessonId",
                table: "UserProgress",
                columns: new[] { "UserId", "LessonId" });
        }
    }
}
