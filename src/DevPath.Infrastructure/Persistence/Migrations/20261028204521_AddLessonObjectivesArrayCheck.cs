using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonObjectivesArrayCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Linhas antigas com JSON que não é lista (ex.: 'null' ou um objeto) viram lista vazia,
            // senão o CHECK não poderia ser criado. O seed regrava os objetivos a partir de /content.
            migrationBuilder.Sql(
                "UPDATE \"Lessons\" SET \"LearningObjectives\" = '[]'::jsonb WHERE jsonb_typeof(\"LearningObjectives\") <> 'array';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Lessons_LearningObjectives_IsArray",
                table: "Lessons",
                sql: "jsonb_typeof(\"LearningObjectives\") = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Lessons_LearningObjectives_IsArray",
                table: "Lessons");
        }
    }
}
