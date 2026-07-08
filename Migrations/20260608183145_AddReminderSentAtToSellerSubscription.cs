using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderSentAtToSellerSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_sent_at",
                table: "seller_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "seller_subscriptions");
        }
    }
}
