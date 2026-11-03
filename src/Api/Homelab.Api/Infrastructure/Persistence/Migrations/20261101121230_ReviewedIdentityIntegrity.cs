using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ReviewedIdentityIntegrity : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__ReviewedIdentityGuard" (
                    "InvalidCount" INTEGER NOT NULL CONSTRAINT "ReviewedIdentityRequiresSameNas" CHECK ("InvalidCount" = 0)
                );
                INSERT INTO "__ReviewedIdentityGuard" SELECT COUNT(*) FROM "DiscoveryAssignments" AS review
                WHERE NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = review."DiskId" AND disk."ConnectionId" = review."ConnectionId");
                INSERT INTO "__ReviewedIdentityGuard" SELECT COUNT(*) FROM "IdentityMerges" AS review
                WHERE NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = review."FirstDiskId" AND disk."ConnectionId" = review."ConnectionId");
                INSERT INTO "__ReviewedIdentityGuard" SELECT COUNT(*) FROM "IdentityMerges" AS review
                WHERE NOT EXISTS (SELECT 1 FROM "Disks" AS disk WHERE disk."Id" = review."SecondDiskId" AND disk."ConnectionId" = review."ConnectionId");
                DROP TABLE "__ReviewedIdentityGuard";
                """);
            migrationBuilder.DropForeignKey(name: "FK_DiscoveryAssignments_Disks_DiskId", table: "DiscoveryAssignments");
            migrationBuilder.DropForeignKey(name: "FK_IdentityMerges_Disks_FirstDiskId", table: "IdentityMerges");
            migrationBuilder.DropForeignKey(name: "FK_IdentityMerges_Disks_SecondDiskId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_IdentityMerges_FirstDiskId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_IdentityMerges_SecondDiskId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_DiscoveryAssignments_DiskId", table: "DiscoveryAssignments");
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_FirstDiskId_ConnectionId", table: "IdentityMerges", columns: new[] { "FirstDiskId", "ConnectionId" });
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_SecondDiskId_ConnectionId", table: "IdentityMerges", columns: new[] { "SecondDiskId", "ConnectionId" });
            migrationBuilder.CreateIndex(name: "IX_DiscoveryAssignments_DiskId_ConnectionId", table: "DiscoveryAssignments", columns: new[] { "DiskId", "ConnectionId" });
            migrationBuilder.AddForeignKey(name: "FK_DiscoveryAssignments_Disks_DiskId_ConnectionId", table: "DiscoveryAssignments", columns: new[] { "DiskId", "ConnectionId" }, principalTable: "Disks", principalColumns: new[] { "Id", "ConnectionId" }, onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_IdentityMerges_Disks_FirstDiskId_ConnectionId", table: "IdentityMerges", columns: new[] { "FirstDiskId", "ConnectionId" }, principalTable: "Disks", principalColumns: new[] { "Id", "ConnectionId" }, onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_IdentityMerges_Disks_SecondDiskId_ConnectionId", table: "IdentityMerges", columns: new[] { "SecondDiskId", "ConnectionId" }, principalTable: "Disks", principalColumns: new[] { "Id", "ConnectionId" }, onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_DiscoveryAssignments_Disks_DiskId_ConnectionId", table: "DiscoveryAssignments");
            migrationBuilder.DropForeignKey(name: "FK_IdentityMerges_Disks_FirstDiskId_ConnectionId", table: "IdentityMerges");
            migrationBuilder.DropForeignKey(name: "FK_IdentityMerges_Disks_SecondDiskId_ConnectionId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_IdentityMerges_FirstDiskId_ConnectionId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_IdentityMerges_SecondDiskId_ConnectionId", table: "IdentityMerges");
            migrationBuilder.DropIndex(name: "IX_DiscoveryAssignments_DiskId_ConnectionId", table: "DiscoveryAssignments");
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_FirstDiskId", table: "IdentityMerges", column: "FirstDiskId");
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_SecondDiskId", table: "IdentityMerges", column: "SecondDiskId");
            migrationBuilder.CreateIndex(name: "IX_DiscoveryAssignments_DiskId", table: "DiscoveryAssignments", column: "DiskId");
            migrationBuilder.AddForeignKey(name: "FK_DiscoveryAssignments_Disks_DiskId", table: "DiscoveryAssignments", column: "DiskId", principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_IdentityMerges_Disks_FirstDiskId", table: "IdentityMerges", column: "FirstDiskId", principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_IdentityMerges_Disks_SecondDiskId", table: "IdentityMerges", column: "SecondDiskId", principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }
    }
}
