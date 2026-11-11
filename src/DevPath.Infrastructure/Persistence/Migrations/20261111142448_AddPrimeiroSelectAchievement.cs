using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #45: seed do selo "Primeiro SELECT" com o id FIXO <c>AchievementIds.PrimeiroSelect</c>
    /// (5e1ec700-0001-4000-8000-000000000001) e <c>Criteria</c> <c>{"slug":"primeiro-select"}</c>. Sem mudança de schema.
    /// Idempotente (<c>ON CONFLICT DO NOTHING</c>): rodar duas vezes não duplica nem sobrescreve.
    /// Os valores ficam escritos aqui (migration é congelada); um teste confere que batem com <c>AchievementIds</c>.
    /// Quem já tinha acertado antes ganha o selo no backfill silencioso do dashboard (<c>IStudentAchievementService</c>).
    /// </summary>
    public partial class AddPrimeiroSelectAchievement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Achievements" ("Id", "Title", "Description", "Icon", "Criteria")
                VALUES ('5e1ec700-0001-4000-8000-000000000001', 'Primeiro SELECT',
                        'Você fez um banco de dados responder à sua primeira pergunta.', NULL,
                        '{"slug":"primeiro-select"}'::jsonb)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Primeiro os selos dos alunos, depois o selo (não depende do ON DELETE CASCADE).
            migrationBuilder.Sql("""
                DELETE FROM "UserAchievements" WHERE "AchievementId" = '5e1ec700-0001-4000-8000-000000000001';
                DELETE FROM "Achievements" WHERE "Id" = '5e1ec700-0001-4000-8000-000000000001';
                """);
        }
    }
}
