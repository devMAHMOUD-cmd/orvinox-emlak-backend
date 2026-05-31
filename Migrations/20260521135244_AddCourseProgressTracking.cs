using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseProgressTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_lesson_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    watched_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_lesson_progress_pkey", x => x.id);
                    table.ForeignKey(
                        name: "user_lesson_progress_course_lesson_id_fkey",
                        column: x => x.course_lesson_id,
                        principalTable: "course_lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "user_lesson_progress_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_lesson_progress_course_lesson_id",
                table: "user_lesson_progress",
                column: "course_lesson_id");

            migrationBuilder.CreateIndex(
                name: "user_lesson_progress_user_id_course_lesson_id_key",
                table: "user_lesson_progress",
                columns: new[] { "user_id", "course_lesson_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_lesson_progress");
        }
    }
}
