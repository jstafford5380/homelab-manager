using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class Hardware : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(name: "HardwareCpuPercent", table: "Settings", type: "REAL", nullable: false, defaultValue: 0.0);
            migrationBuilder.AddColumn<double>(name: "HardwareMemoryPercent", table: "Settings", type: "REAL", nullable: false, defaultValue: 0.0);
            migrationBuilder.AddColumn<int>(name: "HardwareSustainedMinutes", table: "Settings", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<double>(name: "HardwareTemperatureC", table: "Settings", type: "REAL", nullable: false, defaultValue: 0.0);
            migrationBuilder.CreateTable(name: "HardwareServers", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true), Name = table.Column<string>(type: "TEXT", nullable: false), Host = table.Column<string>(type: "TEXT", nullable: false), Platform = table.Column<string>(type: "TEXT", nullable: false), Version = table.Column<string>(type: "TEXT", nullable: true), UptimeSeconds = table.Column<double>(type: "REAL", nullable: true), LastSuccessUtc = table.Column<DateTime>(type: "TEXT", nullable: true), LastMetricsUtc = table.Column<DateTime>(type: "TEXT", nullable: true), CollectionStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), ConfigurationRevision = table.Column<Guid>(type: "TEXT", nullable: false), ProtectedCommunity = table.Column<string>(type: "TEXT", nullable: true), Connected = table.Column<bool>(type: "INTEGER", nullable: false), NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_HardwareServers", x => x.Id);
                table.ForeignKey(name: "FK_HardwareServers_Connections_ConnectionId", column: x => x.ConnectionId, principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "HardwareComponents", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), ServerId = table.Column<Guid>(type: "TEXT", nullable: false), Revision = table.Column<Guid>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false), DetectedKey = table.Column<string>(type: "TEXT", nullable: true), DetectedName = table.Column<string>(type: "TEXT", nullable: true), DetectedSpec = table.Column<string>(type: "TEXT", nullable: true), DetectedTotalBytes = table.Column<double>(type: "REAL", nullable: true), FanFailed = table.Column<bool>(type: "INTEGER", nullable: true), Name = table.Column<string>(type: "TEXT", nullable: false), Model = table.Column<string>(type: "TEXT", nullable: false), Spec = table.Column<string>(type: "TEXT", nullable: false), Notes = table.Column<string>(type: "TEXT", nullable: false), PricePaid = table.Column<decimal>(type: "TEXT", nullable: true), PurchasedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), InstalledUtc = table.Column<DateTime>(type: "TEXT", nullable: true), WarrantyEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true), DatesEstimated = table.Column<bool>(type: "INTEGER", nullable: false), SlotsJson = table.Column<string>(type: "TEXT", nullable: false), SolderedBytes = table.Column<double>(type: "REAL", nullable: true), MaxSupportedBytes = table.Column<double>(type: "REAL", nullable: true), RetiredUtc = table.Column<DateTime>(type: "TEXT", nullable: true), PredecessorId = table.Column<Guid>(type: "TEXT", nullable: true), LastStatus = table.Column<string>(type: "TEXT", nullable: false), LastWhy = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_HardwareComponents", x => x.Id);
                table.UniqueConstraint("AK_HardwareComponents_Id_ServerId", x => new { x.Id, x.ServerId });
                table.ForeignKey(name: "FK_HardwareComponents_HardwareComponents_PredecessorId_ServerId", columns: x => new { x.PredecessorId, x.ServerId }, principalTable: "HardwareComponents", principalColumns: new[] { "Id", "ServerId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_HardwareComponents_HardwareServers_ServerId", column: x => x.ServerId, principalTable: "HardwareServers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "HardwareHourly", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), ServerId = table.Column<Guid>(type: "TEXT", nullable: false), ConfigurationRevision = table.Column<Guid>(type: "TEXT", nullable: false), HourUtc = table.Column<DateTime>(type: "TEXT", nullable: false), SampleCount = table.Column<int>(type: "INTEGER", nullable: false), CpuAverage = table.Column<double>(type: "REAL", nullable: true), AppsAverageBytes = table.Column<double>(type: "REAL", nullable: true), CacheAverageBytes = table.Column<double>(type: "REAL", nullable: true), FreeAverageBytes = table.Column<double>(type: "REAL", nullable: true), TemperatureAverageC = table.Column<double>(type: "REAL", nullable: true), TemperatureMaxC = table.Column<double>(type: "REAL", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_HardwareHourly", x => x.Id);
                table.ForeignKey(name: "FK_HardwareHourly_HardwareServers_ServerId", column: x => x.ServerId, principalTable: "HardwareServers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "HardwareReadings", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), ServerId = table.Column<Guid>(type: "TEXT", nullable: false), Revision = table.Column<Guid>(type: "TEXT", nullable: false), ConfigurationRevision = table.Column<Guid>(type: "TEXT", nullable: false), TakenUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Source = table.Column<string>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false), PeriodMinutes = table.Column<int>(type: "INTEGER", nullable: true), CpuPercent = table.Column<double>(type: "REAL", nullable: true), MemoryUsedBytes = table.Column<double>(type: "REAL", nullable: true), TotalBytes = table.Column<double>(type: "REAL", nullable: true), CacheBytes = table.Column<double>(type: "REAL", nullable: true), FreeBytes = table.Column<double>(type: "REAL", nullable: true), TemperatureC = table.Column<double>(type: "REAL", nullable: true), PowerWatts = table.Column<double>(type: "REAL", nullable: true), Note = table.Column<string>(type: "TEXT", nullable: false), DeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_HardwareReadings", x => x.Id);
                table.ForeignKey(name: "FK_HardwareReadings_HardwareServers_ServerId", column: x => x.ServerId, principalTable: "HardwareServers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "HardwareEvents", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), ComponentId = table.Column<Guid>(type: "TEXT", nullable: false), AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false), Text = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_HardwareEvents", x => x.Id);
                table.ForeignKey(name: "FK_HardwareEvents_HardwareComponents_ComponentId", column: x => x.ComponentId, principalTable: "HardwareComponents", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_HardwareComponents_PredecessorId_ServerId", table: "HardwareComponents", columns: new[] { "PredecessorId", "ServerId" });
            migrationBuilder.CreateIndex(name: "IX_HardwareComponents_ServerId_DetectedKey", table: "HardwareComponents", columns: new[] { "ServerId", "DetectedKey" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_HardwareEvents_ComponentId_AtUtc", table: "HardwareEvents", columns: new[] { "ComponentId", "AtUtc" });
            migrationBuilder.CreateIndex(name: "IX_HardwareHourly_ServerId_ConfigurationRevision_HourUtc", table: "HardwareHourly", columns: new[] { "ServerId", "ConfigurationRevision", "HourUtc" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_HardwareReadings_ServerId_TakenUtc_Source", table: "HardwareReadings", columns: new[] { "ServerId", "TakenUtc", "Source" });
            migrationBuilder.CreateIndex(name: "IX_HardwareServers_ConnectionId", table: "HardwareServers", column: "ConnectionId", unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "HardwareEvents");
            migrationBuilder.DropTable(name: "HardwareHourly");
            migrationBuilder.DropTable(name: "HardwareReadings");
            migrationBuilder.DropTable(name: "HardwareComponents");
            migrationBuilder.DropTable(name: "HardwareServers");
            migrationBuilder.DropColumn(name: "HardwareCpuPercent", table: "Settings");
            migrationBuilder.DropColumn(name: "HardwareMemoryPercent", table: "Settings");
            migrationBuilder.DropColumn(name: "HardwareSustainedMinutes", table: "Settings");
            migrationBuilder.DropColumn(name: "HardwareTemperatureC", table: "Settings");
        }
    }
}
