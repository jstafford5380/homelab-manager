using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class CollectionDailyRetention : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "CollectionDailySummaries", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), GroupKey = table.Column<string>(type: "TEXT", nullable: false), ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false), DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Result = table.Column<string>(type: "TEXT", nullable: false), CapabilitiesKnown = table.Column<bool>(type: "INTEGER", nullable: false), CapabilitiesJson = table.Column<string>(type: "TEXT", nullable: false), AttemptCount = table.Column<long>(type: "INTEGER", nullable: false), FirstStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), LastStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), DurationKnownCount = table.Column<long>(type: "INTEGER", nullable: false), DurationMinMs = table.Column<double>(type: "REAL", nullable: true), DurationMaxMs = table.Column<double>(type: "REAL", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_CollectionDailySummaries", x => x.Id);
            });
            migrationBuilder.CreateIndex(name: "IX_PoolDailySummaries_DayUtc", table: "PoolDailySummaries", column: "DayUtc");
            migrationBuilder.CreateIndex(name: "IX_CollectionRuns_ConnectionId_Id", table: "CollectionRuns", columns: new[] { "ConnectionId", "Id" });
            migrationBuilder.CreateIndex(name: "IX_CollectionRuns_StartedUtc_Id", table: "CollectionRuns", columns: new[] { "StartedUtc", "Id" });
            migrationBuilder.CreateIndex(name: "IX_CollectionDailySummaries_ConnectionId_DayUtc_Id", table: "CollectionDailySummaries", columns: new[] { "ConnectionId", "DayUtc", "Id" });
            migrationBuilder.CreateIndex(name: "IX_CollectionDailySummaries_DayUtc", table: "CollectionDailySummaries", column: "DayUtc");
            migrationBuilder.CreateIndex(name: "IX_CollectionDailySummaries_GroupKey", table: "CollectionDailySummaries", column: "GroupKey", unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CollectionDailySummaries");
            migrationBuilder.DropIndex(name: "IX_PoolDailySummaries_DayUtc", table: "PoolDailySummaries");
            migrationBuilder.DropIndex(name: "IX_CollectionRuns_ConnectionId_Id", table: "CollectionRuns");
            migrationBuilder.DropIndex(name: "IX_CollectionRuns_StartedUtc_Id", table: "CollectionRuns");
        }
    }
}
