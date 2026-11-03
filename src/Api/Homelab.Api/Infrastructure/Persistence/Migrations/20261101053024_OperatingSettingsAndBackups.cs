using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class OperatingSettingsAndBackups : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "Backups", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), FileName = table.Column<string>(type: "TEXT", nullable: false), Status = table.Column<string>(type: "TEXT", nullable: false), Sha256 = table.Column<string>(type: "TEXT", nullable: true), SizeBytes = table.Column<long>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_Backups", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "Settings", columns: table => new { Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), Timezone = table.Column<string>(type: "TEXT", nullable: false), CapacityUnits = table.Column<string>(type: "TEXT", nullable: false), InventoryMinutes = table.Column<int>(type: "INTEGER", nullable: false), SmartMinutes = table.Column<int>(type: "INTEGER", nullable: false), SampleRetentionDays = table.Column<int>(type: "INTEGER", nullable: false), RawRetentionDays = table.Column<int>(type: "INTEGER", nullable: false), BackupsEnabled = table.Column<bool>(type: "INTEGER", nullable: false), BackupDailyCopies = table.Column<int>(type: "INTEGER", nullable: false), BackupWeeklyCopies = table.Column<int>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_Settings", x => x.Id);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Backups");
            migrationBuilder.DropTable(name: "Settings");
        }
    }
}
