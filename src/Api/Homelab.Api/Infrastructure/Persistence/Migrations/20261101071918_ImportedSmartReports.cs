using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ImportedSmartReports : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "SmartReports", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), ReportHash = table.Column<string>(type: "TEXT", nullable: false), ImportedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Actor = table.Column<string>(type: "TEXT", nullable: false), CuratedJson = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_SmartReports", x => x.Id);
                table.ForeignKey(name: "FK_SmartReports_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_SmartReports_DiskId_ReportHash", table: "SmartReports", columns: new[] { "DiskId", "ReportHash" }, unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SmartReports");
        }
    }
}
