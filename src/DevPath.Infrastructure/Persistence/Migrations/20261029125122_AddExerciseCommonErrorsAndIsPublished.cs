using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseCommonErrorsAndIsPublished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommonErrors",
                table: "Exercises",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Exercises",
                type: "boolean",
                nullable: false,
                // Exercícios que já existem continuam visíveis; o seed despublica os que não têm mais .exercise.json.
                // (Editado à mão: o EF geraria false. Mesmo caso de AddLessonIsPublished.)
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exercises_CommonErrors_IsArray",
                table: "Exercises",
                sql: "jsonb_typeof(\"CommonErrors\") = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Exercises_CommonErrors_IsArray",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "CommonErrors",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Exercises");
        }
    }
}
