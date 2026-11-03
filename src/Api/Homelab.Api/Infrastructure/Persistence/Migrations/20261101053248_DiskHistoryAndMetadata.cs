using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DiskHistoryAndMetadata : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BayEvidence", table: "Disks", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "BayVerifiedUtc", table: "Disks", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Lifecycle", table: "Disks", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "PurchaseDateConfidence", table: "Disks", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.CreateTable(name: "DiskAliases", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), Path = table.Column<string>(type: "TEXT", nullable: false), FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false), LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false), EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_DiskAliases", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "DiskAnnotations", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), DiskId = table.Column<Guid>(type: "TEXT", nullable: false), Actor = table.Column<string>(type: "TEXT", nullable: false), AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false), BeforeJson = table.Column<string>(type: "TEXT", nullable: false), AfterJson = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_DiskAnnotations", x => x.Id);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DiskAliases");
            migrationBuilder.DropTable(name: "DiskAnnotations");
            migrationBuilder.DropColumn(name: "BayEvidence", table: "Disks");
            migrationBuilder.DropColumn(name: "BayVerifiedUtc", table: "Disks");
            migrationBuilder.DropColumn(name: "Lifecycle", table: "Disks");
            migrationBuilder.DropColumn(name: "PurchaseDateConfidence", table: "Disks");
        }
    }
}
