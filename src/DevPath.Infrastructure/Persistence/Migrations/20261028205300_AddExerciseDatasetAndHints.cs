using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseDatasetAndHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dataset",
                table: "Exercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hints",
                table: "Exercises",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exercises_Hints_IsArray",
                table: "Exercises",
                sql: "jsonb_typeof(\"Hints\") = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Exercises_Hints_IsArray",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Dataset",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Hints",
                table: "Exercises");
        }
    }
}
