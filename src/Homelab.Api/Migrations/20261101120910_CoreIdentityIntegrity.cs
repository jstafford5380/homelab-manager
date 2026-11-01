using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class CoreIdentityIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stop before any SQLite rebuild; neither discard nor reassign contradictory evidence.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__CoreIdentityGuard" (
                    "InvalidCount" INTEGER NOT NULL CONSTRAINT "CoreEvidenceRequiresMatchingIdentity" CHECK ("InvalidCount" = 0)
                );
                INSERT INTO "__CoreIdentityGuard" SELECT COUNT(*) FROM "Disks" AS record
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = record."ConnectionId");
                INSERT INTO "__CoreIdentityGuard" SELECT COUNT(*) FROM "Observations" AS record
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = record."ConnectionId");
                INSERT INTO "__CoreIdentityGuard" SELECT COUNT(*) FROM "Findings" AS record
                WHERE NOT EXISTS (SELECT 1 FROM "Connections" AS nas WHERE nas."Id" = record."ConnectionId");
                INSERT INTO "__CoreIdentityGuard" SELECT COUNT(*) FROM "Observations" AS record
                WHERE record."DiskId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = record."DiskId" AND disk."ConnectionId" = record."ConnectionId");
                INSERT INTO "__CoreIdentityGuard" SELECT COUNT(*) FROM "Findings" AS record
                WHERE record."DiskId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = record."DiskId" AND disk."ConnectionId" = record."ConnectionId");
                DROP TABLE "__CoreIdentityGuard";
                """);
            // Child-table copies can precede the parent swap. The old parent must already expose this composite key.
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_CoreIdentityUpgradeKey\" ON \"Disks\" (\"Id\", \"ConnectionId\");");
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Disks_Id_ConnectionId",
                table: "Disks",
                columns: new[] { "Id", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ConnectionId",
                table: "Observations",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_DiskId_ConnectionId",
                table: "Observations",
                columns: new[] { "DiskId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ConnectionId",
                table: "Findings",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_DiskId_ConnectionId",
                table: "Findings",
                columns: new[] { "DiskId", "ConnectionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Disks_Connections_ConnectionId",
                table: "Disks",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_Connections_ConnectionId",
                table: "Findings",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_Disks_DiskId_ConnectionId",
                table: "Findings",
                columns: new[] { "DiskId", "ConnectionId" },
                principalTable: "Disks",
                principalColumns: new[] { "Id", "ConnectionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Observations_Connections_ConnectionId",
                table: "Observations",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Observations_Disks_DiskId_ConnectionId",
                table: "Observations",
                columns: new[] { "DiskId", "ConnectionId" },
                principalTable: "Disks",
                principalColumns: new[] { "Id", "ConnectionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Disks_Connections_ConnectionId",
                table: "Disks");

            migrationBuilder.DropForeignKey(
                name: "FK_Findings_Connections_ConnectionId",
                table: "Findings");

            migrationBuilder.DropForeignKey(
                name: "FK_Findings_Disks_DiskId_ConnectionId",
                table: "Findings");

            migrationBuilder.DropForeignKey(
                name: "FK_Observations_Connections_ConnectionId",
                table: "Observations");

            migrationBuilder.DropForeignKey(
                name: "FK_Observations_Disks_DiskId_ConnectionId",
                table: "Observations");

            migrationBuilder.DropIndex(
                name: "IX_Observations_ConnectionId",
                table: "Observations");

            migrationBuilder.DropIndex(
                name: "IX_Observations_DiskId_ConnectionId",
                table: "Observations");

            migrationBuilder.DropIndex(
                name: "IX_Findings_ConnectionId",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_Findings_DiskId_ConnectionId",
                table: "Findings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Disks_Id_ConnectionId",
                table: "Disks");
        }
    }
}
