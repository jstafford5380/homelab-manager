using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScanErrorPeaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ScanErrorsPeak",
                table: "PoolScans",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE PoolScans SET ScanErrorsPeak = (
                    SELECT MAX(ErrorCount) FROM (
                        SELECT PoolScans.ScanErrors AS ErrorCount
                        UNION ALL
                        SELECT p.ScanErrors FROM PoolSnapshots p
                        WHERE p.ConnectionId = PoolScans.ConnectionId AND p.PoolGuid = PoolScans.PoolGuid
                          AND p.ScanFunction = PoolScans.ScanFunction AND p.ScanStartedUtc = PoolScans.ScanStartedUtc
                          AND p.ScanState IN ('SCANNING','PAUSED','FINISHED','CANCELED')
                          AND p.FetchedUtc >= p.ScanStartedUtc
                    ) WHERE ErrorCount BETWEEN 0 AND 9007199254740991
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScanErrorsPeak",
                table: "PoolScans");
        }
    }
}
