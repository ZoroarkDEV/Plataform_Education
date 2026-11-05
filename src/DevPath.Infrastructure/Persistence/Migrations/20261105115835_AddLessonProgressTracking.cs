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

            // Antes do índice único (aluno, aula): se houver linhas repetidas, fica a mais avançada
            // (Mastered > Completed > InProgress > NotStarted); empate = a aberta por último; depois o Id.
            migrationBuilder.Sql("""
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
