using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class DriveRecordIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve legacy evidence: reject dangling references before SQLite rebuilds.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__DriveRecordGuard" (
                    "MissingDiskCount" INTEGER NOT NULL CONSTRAINT "DriveRecordRequiresDisk" CHECK ("MissingDiskCount" = 0)
                );
                INSERT INTO "__DriveRecordGuard" SELECT COUNT(*) FROM "Plans" AS record
                WHERE record."DiskId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = record."DiskId");
                INSERT INTO "__DriveRecordGuard" SELECT COUNT(*) FROM "DiskAliases" AS record
                WHERE record."DiskId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = record."DiskId");
                INSERT INTO "__DriveRecordGuard" SELECT COUNT(*) FROM "DiskAnnotations" AS record
                WHERE record."DiskId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = record."DiskId");
                DROP TABLE "__DriveRecordGuard";
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Plans_DiskId",
                table: "Plans",
                column: "DiskId");

            migrationBuilder.CreateIndex(
                name: "IX_DiskAnnotations_DiskId",
                table: "DiskAnnotations",
                column: "DiskId");

            migrationBuilder.CreateIndex(
                name: "IX_DiskAliases_DiskId",
                table: "DiskAliases",
                column: "DiskId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiskAliases_Disks_DiskId",
                table: "DiskAliases",
                column: "DiskId",
                principalTable: "Disks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiskAnnotations_Disks_DiskId",
                table: "DiskAnnotations",
                column: "DiskId",
                principalTable: "Disks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Disks_DiskId",
                table: "Plans",
                column: "DiskId",
                principalTable: "Disks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiskAliases_Disks_DiskId",
                table: "DiskAliases");

            migrationBuilder.DropForeignKey(
                name: "FK_DiskAnnotations_Disks_DiskId",
                table: "DiskAnnotations");

            migrationBuilder.DropForeignKey(
                name: "FK_Plans_Disks_DiskId",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Plans_DiskId",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_DiskAnnotations_DiskId",
                table: "DiskAnnotations");

            migrationBuilder.DropIndex(
                name: "IX_DiskAliases_DiskId",
                table: "DiskAliases");
        }
    }
}
