using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewVideoToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preview_video_url",
                table: "products",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preview_video_url",
                table: "products");
        }
    }
}
