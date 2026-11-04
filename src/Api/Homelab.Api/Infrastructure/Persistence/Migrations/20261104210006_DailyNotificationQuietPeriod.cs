using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyNotificationQuietPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuietEnd",
                table: "NotificationChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuietStart",
                table: "NotificationChannels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuietEnd",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "QuietStart",
                table: "NotificationChannels");
        }
    }
}
