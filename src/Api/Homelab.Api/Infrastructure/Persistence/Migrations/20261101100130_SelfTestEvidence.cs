using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class SelfTestEvidence : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "SelfTestEvidence", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), ReportId = table.Column<Guid>(type: "TEXT", nullable: false), Fingerprint = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_SelfTestEvidence", x => x.Id);
                table.ForeignKey(name: "FK_SelfTestEvidence_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_SelfTestEvidence_SmartReports_ReportId", column: x => x.ReportId, principalTable: "SmartReports", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_SelfTestEvidence_DiskId_Fingerprint", table: "SelfTestEvidence", columns: new[] { "DiskId", "Fingerprint" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_SelfTestEvidence_ReportId", table: "SelfTestEvidence", column: "ReportId");
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SelfTestEvidence");
        }
    }
}
