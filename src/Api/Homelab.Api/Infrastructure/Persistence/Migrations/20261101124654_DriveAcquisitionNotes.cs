using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DriveAcquisitionNotes : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "DriveAcquisitions", columns: table => new { DiskId = table.Column<Guid>(type: "TEXT", nullable: false), Revision = table.Column<Guid>(type: "TEXT", nullable: false), InstalledUtc = table.Column<DateTime>(type: "TEXT", nullable: true), InstallationConfidence = table.Column<string>(type: "TEXT", nullable: false), InstallationEvidence = table.Column<string>(type: "TEXT", nullable: false), PurchaseCost = table.Column<decimal>(type: "TEXT", nullable: true), Currency = table.Column<string>(type: "TEXT", nullable: false), CostConfidence = table.Column<string>(type: "TEXT", nullable: false), PurchaseEvidence = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_DriveAcquisitions", x => x.DiskId);
                table.ForeignKey(name: "FK_DriveAcquisitions_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DriveAcquisitions");
        }
    }
}
