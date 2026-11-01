using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class PoolDailyRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoolDailySummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupKey = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PoolGuid = table.Column<string>(type: "TEXT", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceSchema = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ScanFunction = table.Column<string>(type: "TEXT", nullable: true),
                    ScanState = table.Column<string>(type: "TEXT", nullable: true),
                    SampleCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstFetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SizeBytesKnownCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytesMin = table.Column<long>(type: "INTEGER", nullable: true),
                    SizeBytesMax = table.Column<long>(type: "INTEGER", nullable: true),
                    AllocatedBytesKnownCount = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedBytesMin = table.Column<long>(type: "INTEGER", nullable: true),
                    AllocatedBytesMax = table.Column<long>(type: "INTEGER", nullable: true),
                    FreeBytesKnownCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FreeBytesMin = table.Column<long>(type: "INTEGER", nullable: true),
                    FreeBytesMax = table.Column<long>(type: "INTEGER", nullable: true),
                    ScanErrorsKnownCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ScanErrorsMin = table.Column<long>(type: "INTEGER", nullable: true),
                    ScanErrorsMax = table.Column<long>(type: "INTEGER", nullable: true),
                    ScanPercentageKnownCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ScanPercentageMin = table.Column<double>(type: "REAL", nullable: true),
                    ScanPercentageMax = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolDailySummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoolDailySummaries_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoolSnapshots_FetchedUtc_Id",
                table: "PoolSnapshots",
                columns: new[] { "FetchedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PoolDailySummaries_ConnectionId_PoolGuid_DayUtc_Id",
                table: "PoolDailySummaries",
                columns: new[] { "ConnectionId", "PoolGuid", "DayUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PoolDailySummaries_GroupKey",
                table: "PoolDailySummaries",
                column: "GroupKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolDailySummaries");

            migrationBuilder.DropIndex(
                name: "IX_PoolSnapshots_FetchedUtc_Id",
                table: "PoolSnapshots");
        }
    }
}
