using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class CollectionSourceIntegrity : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate before SQLite rebuilds disable foreign-key enforcement; preserve all evidence on failure.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__CollectionSourceGuard" (
                    "MissingSourceCount" INTEGER NOT NULL CONSTRAINT "CollectionEvidenceRequiresNas" CHECK ("MissingSourceCount" = 0)
                );
                INSERT INTO "__CollectionSourceGuard" SELECT COUNT(*) FROM "Capabilities" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = evidence."ConnectionId");
                INSERT INTO "__CollectionSourceGuard" SELECT COUNT(*) FROM "NativeAlerts" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = evidence."ConnectionId");
                INSERT INTO "__CollectionSourceGuard" SELECT COUNT(*) FROM "CollectionRuns" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = evidence."ConnectionId");
                INSERT INTO "__CollectionSourceGuard" SELECT COUNT(*) FROM "CollectionDailySummaries" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = evidence."ConnectionId");
                DROP TABLE "__CollectionSourceGuard";
                """);
            migrationBuilder.AddForeignKey(name: "FK_Capabilities_Connections_ConnectionId", table: "Capabilities", column: "ConnectionId", principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_CollectionDailySummaries_Connections_ConnectionId", table: "CollectionDailySummaries", column: "ConnectionId", principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_CollectionRuns_Connections_ConnectionId", table: "CollectionRuns", column: "ConnectionId", principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_NativeAlerts_Connections_ConnectionId", table: "NativeAlerts", column: "ConnectionId", principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Capabilities_Connections_ConnectionId", table: "Capabilities");
            migrationBuilder.DropForeignKey(name: "FK_CollectionDailySummaries_Connections_ConnectionId", table: "CollectionDailySummaries");
            migrationBuilder.DropForeignKey(name: "FK_CollectionRuns_Connections_ConnectionId", table: "CollectionRuns");
            migrationBuilder.DropForeignKey(name: "FK_NativeAlerts_Connections_ConnectionId", table: "NativeAlerts");
        }
    }
}
