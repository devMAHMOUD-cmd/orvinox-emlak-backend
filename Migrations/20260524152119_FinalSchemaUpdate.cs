using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class FinalSchemaUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:public.media_status", "processing,ready,failed")
                .Annotation("Npgsql:Enum:public.order_status", "pending,completed,failed,refunded")
                .Annotation("Npgsql:Enum:public.payment_status_type", "processing,succeeded,failed,refunded")
                .Annotation("Npgsql:Enum:public.product_type", "digital_file,course")
                .Annotation("Npgsql:Enum:public.sub_status", "active,past_due,canceled,unpaid")
                .Annotation("Npgsql:Enum:public.user_role", "user,seller,admin")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .OldAnnotation("Npgsql:Enum:media_status", "failed,processing,ready")
                .OldAnnotation("Npgsql:Enum:order_status", "completed,failed,pending,refunded")
                .OldAnnotation("Npgsql:Enum:payment_status_type", "failed,processing,refunded,succeeded")
                .OldAnnotation("Npgsql:Enum:product_type", "course,digital_file")
                .OldAnnotation("Npgsql:Enum:public.media_status", "processing,ready,failed")
                .OldAnnotation("Npgsql:Enum:public.order_status", "pending,completed,failed,refunded")
                .OldAnnotation("Npgsql:Enum:public.payment_status_type", "processing,succeeded,failed,refunded")
                .OldAnnotation("Npgsql:Enum:public.product_type", "digital_file,course")
                .OldAnnotation("Npgsql:Enum:public.sub_status", "active,past_due,canceled,unpaid")
                .OldAnnotation("Npgsql:Enum:public.user_role", "user,seller,admin")
                .OldAnnotation("Npgsql:Enum:sub_status", "active,canceled,past_due,unpaid")
                .OldAnnotation("Npgsql:Enum:user_role", "admin,seller,user")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "media",
                type: "media_status",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "media");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:media_status", "failed,processing,ready")
                .Annotation("Npgsql:Enum:order_status", "completed,failed,pending,refunded")
                .Annotation("Npgsql:Enum:payment_status_type", "failed,processing,refunded,succeeded")
                .Annotation("Npgsql:Enum:product_type", "course,digital_file")
                .Annotation("Npgsql:Enum:public.media_status", "processing,ready,failed")
                .Annotation("Npgsql:Enum:public.order_status", "pending,completed,failed,refunded")
                .Annotation("Npgsql:Enum:public.payment_status_type", "processing,succeeded,failed,refunded")
                .Annotation("Npgsql:Enum:public.product_type", "digital_file,course")
                .Annotation("Npgsql:Enum:public.sub_status", "active,past_due,canceled,unpaid")
                .Annotation("Npgsql:Enum:public.user_role", "user,seller,admin")
                .Annotation("Npgsql:Enum:sub_status", "active,canceled,past_due,unpaid")
                .Annotation("Npgsql:Enum:user_role", "admin,seller,user")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .OldAnnotation("Npgsql:Enum:public.media_status", "processing,ready,failed")
                .OldAnnotation("Npgsql:Enum:public.order_status", "pending,completed,failed,refunded")
                .OldAnnotation("Npgsql:Enum:public.payment_status_type", "processing,succeeded,failed,refunded")
                .OldAnnotation("Npgsql:Enum:public.product_type", "digital_file,course")
                .OldAnnotation("Npgsql:Enum:public.sub_status", "active,past_due,canceled,unpaid")
                .OldAnnotation("Npgsql:Enum:public.user_role", "user,seller,admin")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,");
        }
    }
}
