using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class SourceScanTiming : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "ScanEndedUtc", table: "PoolSnapshots", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ScanPausedUtc", table: "PoolSnapshots", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<long>(name: "ScanRemainingSeconds", table: "PoolSnapshots", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ScanStartedUtc", table: "PoolSnapshots", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ScanEndedUtc", table: "PoolSnapshots");
            migrationBuilder.DropColumn(name: "ScanPausedUtc", table: "PoolSnapshots");
            migrationBuilder.DropColumn(name: "ScanRemainingSeconds", table: "PoolSnapshots");
            migrationBuilder.DropColumn(name: "ScanStartedUtc", table: "PoolSnapshots");
        }
    }
}
