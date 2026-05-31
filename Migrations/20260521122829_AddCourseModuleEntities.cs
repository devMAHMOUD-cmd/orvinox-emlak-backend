using System;
using CraftoraApi.Models.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseModuleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("TRUNCATE TABLE course_lessons CASCADE;");
            migrationBuilder.Sql("TRUNCATE TABLE course_sections CASCADE;");

            migrationBuilder.DropForeignKey(
                name: "course_sections_product_id_fkey",
                table: "course_sections");

            migrationBuilder.DropColumn(
                name: "document_url",
                table: "course_lessons");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "course_lessons");

            migrationBuilder.DropColumn(
                name: "status",
                table: "course_lessons");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "course_sections",
                newName: "course_id");

            migrationBuilder.RenameIndex(
                name: "idx_course_sections_product",
                table: "course_sections",
                newName: "idx_course_sections_course");

            migrationBuilder.RenameIndex(
                name: "course_sections_product_id_sort_order_key",
                table: "course_sections",
                newName: "course_sections_course_id_sort_order_key");

            migrationBuilder.RenameColumn(
                name: "section_id",
                table: "course_lessons",
                newName: "course_section_id");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "course_sections",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "course_sections",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "course_sections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_free_preview",
                table: "course_lessons",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldDefaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "course_lessons",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "duration_in_seconds",
                table: "course_lessons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "course_lessons",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "course_lessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "course_quizzes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    course_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    passing_score = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("course_quizzes_pkey", x => x.id);
                    table.ForeignKey(
                        name: "course_quizzes_section_id_fkey",
                        column: x => x.course_section_id,
                        principalTable: "course_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_duration_in_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_certificate_included = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("courses_pkey", x => x.id);
                    table.ForeignKey(
                        name: "courses_product_id_fkey",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lesson_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    course_lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("lesson_resources_pkey", x => x.id);
                    table.ForeignKey(
                        name: "lesson_resources_lesson_id_fkey",
                        column: x => x.course_lesson_id,
                        principalTable: "course_lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_course_quizzes_section",
                table: "course_quizzes",
                column: "course_section_id");

            migrationBuilder.CreateIndex(
                name: "idx_courses_product",
                table: "courses",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "idx_lesson_resources_lesson",
                table: "lesson_resources",
                column: "course_lesson_id");

            migrationBuilder.AddForeignKey(
                name: "course_sections_course_id_fkey",
                table: "course_sections",
                column: "course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "course_sections_course_id_fkey",
                table: "course_sections");

            migrationBuilder.DropTable(
                name: "course_quizzes");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "lesson_resources");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "course_sections");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "course_sections");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "course_sections");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "course_lessons");

            migrationBuilder.DropColumn(
                name: "duration_in_seconds",
                table: "course_lessons");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "course_lessons");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "course_lessons");

            migrationBuilder.RenameColumn(
                name: "course_id",
                table: "course_sections",
                newName: "product_id");

            migrationBuilder.RenameIndex(
                name: "idx_course_sections_course",
                table: "course_sections",
                newName: "idx_course_sections_product");

            migrationBuilder.RenameIndex(
                name: "course_sections_course_id_sort_order_key",
                table: "course_sections",
                newName: "course_sections_product_id_sort_order_key");

            migrationBuilder.RenameColumn(
                name: "course_section_id",
                table: "course_lessons",
                newName: "section_id");

            migrationBuilder.AlterColumn<bool>(
                name: "is_free_preview",
                table: "course_lessons",
                type: "boolean",
                nullable: true,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "document_url",
                table: "course_lessons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_seconds",
                table: "course_lessons",
                type: "integer",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.AddColumn<MediaStatus>(
                name: "status",
                table: "course_lessons",
                type: "media_status",
                nullable: false,
                defaultValue: MediaStatus.Ready);

            migrationBuilder.AddForeignKey(
                name: "course_sections_product_id_fkey",
                table: "course_sections",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
