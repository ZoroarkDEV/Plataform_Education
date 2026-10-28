using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentOneActivePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Antes da #11 nada criava matrícula, mas por segurança: se um aluno tiver mais de uma ativa,
            // fica ativa a da trilha atual dele (AspNetUsers.CurrentPathId) ou, sem ela, a mais recente.
            migrationBuilder.Sql("""
                UPDATE "UserPathEnrollments" e
                SET "IsActive" = FALSE
                WHERE e."IsActive"
                  AND EXISTS (
                      SELECT 1
                      FROM "UserPathEnrollments" o
                      LEFT JOIN "AspNetUsers" u ON u."Id" = o."UserId"
                      WHERE o."UserId" = e."UserId" AND o."IsActive" AND o."CareerPathId" <> e."CareerPathId"
                        AND (
                            o."CareerPathId" = u."CurrentPathId"
                            OR (e."CareerPathId" IS DISTINCT FROM u."CurrentPathId"
                                AND (o."EnrolledAt", o."CareerPathId") > (e."EnrolledAt", e."CareerPathId"))
                        )
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserPathEnrollments_UserId_Active",
                table: "UserPathEnrollments",
                column: "UserId",
                unique: true,
                filter: "\"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPathEnrollments_UserId_Active",
                table: "UserPathEnrollments");
        }
    }
}
