using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DiskReplacementHistory : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "Replacements", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), OldDiskId = table.Column<Guid>(type: "TEXT", nullable: false), NewDiskId = table.Column<Guid>(type: "TEXT", nullable: false), OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false), DateConfidence = table.Column<string>(type: "TEXT", nullable: false), PoolGuid = table.Column<string>(type: "TEXT", nullable: false), MembershipEvidence = table.Column<string>(type: "TEXT", nullable: false), Reason = table.Column<string>(type: "TEXT", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), OldIdentity = table.Column<string>(type: "TEXT", nullable: false), NewIdentity = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), VoidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), VoidReason = table.Column<string>(type: "TEXT", nullable: true), VoidedBy = table.Column<string>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_Replacements", x => x.Id);
                table.ForeignKey(name: "FK_Replacements_Disks_NewDiskId", column: x => x.NewDiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_Replacements_Disks_OldDiskId", column: x => x.OldDiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_Replacements_NewDiskId_OccurredUtc", table: "Replacements", columns: new[] { "NewDiskId", "OccurredUtc" });
            migrationBuilder.CreateIndex(name: "IX_Replacements_OldDiskId_OccurredUtc", table: "Replacements", columns: new[] { "OldDiskId", "OccurredUtc" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Replacements");
        }
    }
}
