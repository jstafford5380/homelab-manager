using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class SpareCompatibilityReviews : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "SpareReviews", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), AssetId = table.Column<Guid>(type: "TEXT", nullable: false), RequestId = table.Column<Guid>(type: "TEXT", nullable: false), AssetUpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), ConfirmedIdentity = table.Column<string>(type: "TEXT", nullable: false), DiskModel = table.Column<string>(type: "TEXT", nullable: false), AssetName = table.Column<string>(type: "TEXT", nullable: false), Verdict = table.Column<string>(type: "TEXT", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), ValidUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_SpareReviews", x => x.Id);
                table.ForeignKey(name: "FK_SpareReviews_Assets_AssetId", column: x => x.AssetId, principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_SpareReviews_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_SpareReviews_AssetId", table: "SpareReviews", column: "AssetId");
            migrationBuilder.CreateIndex(name: "IX_SpareReviews_DiskId_AssetId_Id", table: "SpareReviews", columns: new[] { "DiskId", "AssetId", "Id" });
            migrationBuilder.CreateIndex(name: "IX_SpareReviews_DiskId_RequestId", table: "SpareReviews", columns: new[] { "DiskId", "RequestId" }, unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SpareReviews");
        }
    }
}
