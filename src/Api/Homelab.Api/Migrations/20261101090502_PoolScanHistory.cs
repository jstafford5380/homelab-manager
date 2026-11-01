using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class PoolScanHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoolScans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PoolGuid = table.Column<string>(type: "TEXT", nullable: false),
                    ScanFunction = table.Column<string>(type: "TEXT", nullable: false),
                    ScanStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScanState = table.Column<string>(type: "TEXT", nullable: false),
                    ScanEndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScanPausedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScanRemainingSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ScanPercentage = table.Column<double>(type: "REAL", nullable: true),
                    ScanErrors = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstFetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceSchema = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoolScans_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoolScans_ConnectionId_PoolGuid_Id",
                table: "PoolScans",
                columns: new[] { "ConnectionId", "PoolGuid", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PoolScans_ConnectionId_PoolGuid_ScanFunction_ScanStartedUtc",
                table: "PoolScans",
                columns: new[] { "ConnectionId", "PoolGuid", "ScanFunction", "ScanStartedUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoolScans_LastFetchedUtc",
                table: "PoolScans",
                column: "LastFetchedUtc");
            // Retained snapshots already contain curated source dates. Backfill only
            // identifiable scans; legacy snapshots without source timing stay ungrouped.
            migrationBuilder.Sql("""
                INSERT INTO PoolScans
                    (ConnectionId, PoolGuid, ScanFunction, ScanStartedUtc, ScanState, ScanEndedUtc,
                     ScanPausedUtc, ScanRemainingSeconds, ScanPercentage, ScanErrors, FirstFetchedUtc, LastFetchedUtc, SourceSchema)
                SELECT ConnectionId, PoolGuid, ScanFunction, ScanStartedUtc, ScanState,
                    CASE WHEN ScanState IN ('FINISHED','CANCELED') AND ScanEndedUtc >= ScanStartedUtc AND ScanEndedUtc <= FetchedUtc THEN ScanEndedUtc END,
                    CASE WHEN ScanState IN ('SCANNING','PAUSED') AND ScanPausedUtc >= ScanStartedUtc AND ScanPausedUtc <= FetchedUtc THEN ScanPausedUtc END,
                    CASE WHEN ScanState IN ('SCANNING','PAUSED') AND ScanRemainingSeconds BETWEEN 0 AND 9007199254740991 THEN ScanRemainingSeconds END,
                    CASE WHEN ScanPercentage BETWEEN 0 AND 100 THEN ScanPercentage END,
                    CASE WHEN ScanErrors BETWEEN 0 AND 9007199254740991 THEN ScanErrors END,
                    FirstRead, FetchedUtc, SourceSchema
                FROM (
                    SELECT *,
                        MIN(FetchedUtc) OVER (PARTITION BY ConnectionId, PoolGuid, ScanFunction, ScanStartedUtc) AS FirstRead,
                        ROW_NUMBER() OVER (PARTITION BY ConnectionId, PoolGuid, ScanFunction, ScanStartedUtc ORDER BY FetchedUtc DESC, Id DESC) AS Position
                    FROM PoolSnapshots
                    WHERE ScanStartedUtc > '1970-01-01 00:00:00' AND ScanStartedUtc <= FetchedUtc
                      AND LENGTH(TRIM(PoolGuid)) > 0 AND ScanFunction IN ('SCRUB','RESILVER')
                      AND ScanState IN ('SCANNING','PAUSED','FINISHED','CANCELED')
                ) WHERE Position = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolScans");
        }
    }
}
