using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryFindingIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Channel tests use audit events, not outbox deliveries. Every retained delivery requires its original finding.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__DeliveryFindingGuard" (
                    "InvalidCount" INTEGER NOT NULL CONSTRAINT "DeliveryRequiresFinding" CHECK ("InvalidCount" = 0)
                );
                INSERT INTO "__DeliveryFindingGuard" SELECT COUNT(*) FROM "Deliveries" AS delivery
                WHERE NOT EXISTS (SELECT 1 FROM "Findings" AS finding WHERE finding."Id" = delivery."FindingId");
                DROP TABLE "__DeliveryFindingGuard";
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_FindingId",
                table: "Deliveries",
                column: "FindingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Deliveries_Findings_FindingId",
                table: "Deliveries",
                column: "FindingId",
                principalTable: "Findings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deliveries_Findings_FindingId",
                table: "Deliveries");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_FindingId",
                table: "Deliveries");
        }
    }
}
