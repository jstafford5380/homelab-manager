using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class NotificationOutbox : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "Deliveries", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), ChannelRevision = table.Column<Guid>(type: "TEXT", nullable: false), FindingId = table.Column<Guid>(type: "TEXT", nullable: false), EventKey = table.Column<string>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false), PayloadJson = table.Column<string>(type: "TEXT", nullable: false), Status = table.Column<string>(type: "TEXT", nullable: false), Attempts = table.Column<int>(type: "INTEGER", nullable: false), CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false), AcceptedUtc = table.Column<DateTime>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_Deliveries", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "DeliveryAttempts", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false), AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false), HttpStatus = table.Column<int>(type: "INTEGER", nullable: true), Result = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_DeliveryAttempts", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "NotificationChannels", columns: table => new { Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), Revision = table.Column<Guid>(type: "TEXT", nullable: false), ProtectedUrl = table.Column<string>(type: "TEXT", nullable: false), Host = table.Column<string>(type: "TEXT", nullable: false), Enabled = table.Column<bool>(type: "INTEGER", nullable: false), MaskIdentifiers = table.Column<bool>(type: "INTEGER", nullable: false), TestedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), MaintenanceUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_NotificationChannels", x => x.Id);
            });
            migrationBuilder.CreateIndex(name: "IX_Deliveries_EventKey", table: "Deliveries", column: "EventKey", unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Deliveries");
            migrationBuilder.DropTable(name: "DeliveryAttempts");
            migrationBuilder.DropTable(name: "NotificationChannels");
        }
    }
}
