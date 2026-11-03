using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DriveMaintenance : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "Maintenance", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), RequestId = table.Column<Guid>(type: "TEXT", nullable: false), OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false), DateConfidence = table.Column<string>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false), Outcome = table.Column<string>(type: "TEXT", nullable: false), Summary = table.Column<string>(type: "TEXT", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), ConfirmedIdentity = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), SourceSchema = table.Column<string>(type: "TEXT", nullable: false), VoidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), VoidReason = table.Column<string>(type: "TEXT", nullable: true), VoidedBy = table.Column<string>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_Maintenance", x => x.Id);
                table.ForeignKey(name: "FK_Maintenance_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_Maintenance_DiskId_Id", table: "Maintenance", columns: new[] { "DiskId", "Id" });
            migrationBuilder.CreateIndex(name: "IX_Maintenance_DiskId_RequestId", table: "Maintenance", columns: new[] { "DiskId", "RequestId" }, unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Maintenance");
        }
    }
}
