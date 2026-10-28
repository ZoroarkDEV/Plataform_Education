using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevPath.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Criteria = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareerPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DifficultyLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstimatedHours = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerPaths", x => x.Id);
                    table.CheckConstraint("CK_CareerPaths_EstimatedHours_NonNegative", "\"EstimatedHours\" >= 0");
                    table.CheckConstraint("CK_CareerPaths_Order_NonNegative", "\"Order\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<Guid>(type: "uuid", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievements", x => new { x.UserId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_UserAchievements_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CareerPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                    table.CheckConstraint("CK_Modules_EstimatedMinutes_NonNegative", "\"EstimatedMinutes\" >= 0");
                    table.CheckConstraint("CK_Modules_Order_NonNegative", "\"Order\" >= 0");
                    table.ForeignKey(
                        name: "FK_Modules_CareerPaths_CareerPathId",
                        column: x => x.CareerPathId,
                        principalTable: "CareerPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    CodeLanguage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.CheckConstraint("CK_Lessons_EstimatedMinutes_NonNegative", "\"EstimatedMinutes\" >= 0");
                    table.CheckConstraint("CK_Lessons_Order_NonNegative", "\"Order\" >= 0");
                    table.ForeignKey(
                        name: "FK_Lessons_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPathEnrollments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CareerPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProgressPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CurrentModuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPathEnrollments", x => new { x.UserId, x.CareerPathId });
                    table.CheckConstraint("CK_UserPathEnrollments_ProgressPercentage_Range", "\"ProgressPercentage\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_UserPathEnrollments_CareerPaths_CareerPathId",
                        column: x => x.CareerPathId,
                        principalTable: "CareerPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPathEnrollments_Modules_CurrentModuleId",
                        column: x => x.CurrentModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Statement = table.Column<string>(type: "text", nullable: false),
                    StarterCode = table.Column<string>(type: "text", nullable: false),
                    SolutionCode = table.Column<string>(type: "text", nullable: false),
                    TestCases = table.Column<string>(type: "jsonb", nullable: false),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                    table.CheckConstraint("CK_Exercises_MaxAttempts_Positive", "\"MaxAttempts\" IS NULL OR \"MaxAttempts\" > 0");
                    table.CheckConstraint("CK_Exercises_Points_NonNegative", "\"Points\" >= 0");
                    table.ForeignKey(
                        name: "FK_Exercises_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedCode = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Feedback = table.Column<string>(type: "text", nullable: true),
                    ExecutionTimeMs = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeSubmissions", x => x.Id);
                    table.CheckConstraint("CK_CodeSubmissions_ExecutionTimeMs_NonNegative", "\"ExecutionTimeMs\" >= 0");
                    table.ForeignKey(
                        name: "FK_CodeSubmissions_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProgress", x => x.Id);
                    table.CheckConstraint("CK_UserProgress_ExactlyOneTarget", "num_nonnulls(\"LessonId\", \"ExerciseId\", \"ModuleId\") = 1");
                    table.CheckConstraint("CK_UserProgress_Score_NonNegative", "\"Score\" IS NULL OR \"Score\" >= 0");
                    table.CheckConstraint("CK_UserProgress_TimeSpentSeconds_NonNegative", "\"TimeSpentSeconds\" >= 0");
                    table.ForeignKey(
                        name: "FK_UserProgress_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProgress_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProgress_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerPaths_Slug",
                table: "CareerPaths",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeSubmissions_ExerciseId",
                table: "CodeSubmissions",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeSubmissions_UserId_ExerciseId",
                table: "CodeSubmissions",
                columns: new[] { "UserId", "ExerciseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_LessonId",
                table: "Exercises",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ModuleId_Order",
                table: "Lessons",
                columns: new[] { "ModuleId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ModuleId_Slug",
                table: "Lessons",
                columns: new[] { "ModuleId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modules_CareerPathId_Order",
                table: "Modules",
                columns: new[] { "CareerPathId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modules_CareerPathId_Slug",
                table: "Modules",
                columns: new[] { "CareerPathId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementId",
                table: "UserAchievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPathEnrollments_CareerPathId",
                table: "UserPathEnrollments",
                column: "CareerPathId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPathEnrollments_CurrentModuleId",
                table: "UserPathEnrollments",
                column: "CurrentModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_ExerciseId",
                table: "UserProgress",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_LessonId",
                table: "UserProgress",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_ModuleId",
                table: "UserProgress",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_UserId_LessonId",
                table: "UserProgress",
                columns: new[] { "UserId", "LessonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeSubmissions");

            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "UserPathEnrollments");

            migrationBuilder.DropTable(
                name: "UserProgress");

            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "Modules");

            migrationBuilder.DropTable(
                name: "CareerPaths");
        }
    }
}
