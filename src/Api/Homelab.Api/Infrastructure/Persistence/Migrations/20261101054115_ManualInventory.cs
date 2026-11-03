using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ManualInventory : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "Assets", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), Type = table.Column<string>(type: "TEXT", nullable: false), Name = table.Column<string>(type: "TEXT", nullable: false), Purpose = table.Column<string>(type: "TEXT", nullable: false), Owner = table.Column<string>(type: "TEXT", nullable: false), Criticality = table.Column<string>(type: "TEXT", nullable: false), Location = table.Column<string>(type: "TEXT", nullable: false), Version = table.Column<string>(type: "TEXT", nullable: false), ManagementUrl = table.Column<string>(type: "TEXT", nullable: false), Documentation = table.Column<string>(type: "TEXT", nullable: false), Runbook = table.Column<string>(type: "TEXT", nullable: false), Verification = table.Column<string>(type: "TEXT", nullable: false), Quantity = table.Column<int>(type: "INTEGER", nullable: false), CompatibilityNotes = table.Column<string>(type: "TEXT", nullable: false), EstimatedUnitCost = table.Column<decimal>(type: "TEXT", nullable: true), Currency = table.Column<string>(type: "TEXT", nullable: false), Archived = table.Column<bool>(type: "INTEGER", nullable: false), UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_Assets", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "Dependencies", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), SourceId = table.Column<Guid>(type: "TEXT", nullable: false), TargetId = table.Column<Guid>(type: "TEXT", nullable: false), Relationship = table.Column<string>(type: "TEXT", nullable: false), Verification = table.Column<string>(type: "TEXT", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_Dependencies", x => x.Id);
                table.ForeignKey(name: "FK_Dependencies_Assets_SourceId", column: x => x.SourceId, principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_Dependencies_Assets_TargetId", column: x => x.TargetId, principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_Dependencies_SourceId_TargetId_Relationship", table: "Dependencies", columns: new[] { "SourceId", "TargetId", "Relationship" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_Dependencies_TargetId", table: "Dependencies", column: "TargetId");
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Dependencies");
            migrationBuilder.DropTable(name: "Assets");
        }
    }
}
