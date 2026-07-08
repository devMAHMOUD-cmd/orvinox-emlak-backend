using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("product_images_pkey", x => x.id);
                    table.ForeignKey(
                        name: "product_images_product_id_fkey",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_product_images_product",
                table: "product_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "product_images_product_id_object_key_key",
                table: "product_images",
                columns: new[] { "product_id", "object_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_images");
        }
    }
}
