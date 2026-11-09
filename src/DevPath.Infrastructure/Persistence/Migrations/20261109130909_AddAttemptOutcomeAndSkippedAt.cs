using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptOutcomeAndSkippedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SkippedAt",
                table: "UserProgress",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "CodeSubmissions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // #43: preenche o que dá para saber do que já existe.
            // - Tentativa que passou é certa. As que não passaram ficam com Outcome NULL: antes do #43 erro e timeout
            //   também eram gravados como Fail, então não dá para separar "errado" de "erro" no passado.
            // - Quem está hoje em "Seguir sem pontos" usou o botão quando concluiu (CompletedAt). Quem já foi promovido
            //   para ExercisePassed antes desta migration não tem como ser recuperado.
            migrationBuilder.Sql("""UPDATE "CodeSubmissions" SET "Outcome" = 'Pass' WHERE "Result" = 'Pass';""");
            migrationBuilder.Sql("""
                UPDATE "UserProgress" SET "SkippedAt" = COALESCE("CompletedAt", "LastAccessedAt")
                WHERE "CompletionKind" = 'SkippedAfterFailures';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserProgress_SkippedAt",
                table: "UserProgress",
                sql: "\"SkippedAt\" IS NULL OR \"Status\" IN ('Completed', 'Mastered')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CodeSubmissions_Outcome_MatchesResult",
                table: "CodeSubmissions",
                sql: "(\"Result\" = 'Pass' AND COALESCE(\"Outcome\", '') = 'Pass') OR (\"Result\" = 'Fail' AND COALESCE(\"Outcome\", 'Fail') IN ('Fail', 'Error', 'Timeout'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserProgress_SkippedAt",
                table: "UserProgress");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CodeSubmissions_Outcome_MatchesResult",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "SkippedAt",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "CodeSubmissions");
        }
    }
}
