using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations;
public partial class RestrictedSmartRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "SmartReads", columns: table => new { DiskId = table.Column<Guid>(type: "TEXT", nullable: false), AttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false), ReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true), SkippedAsleep = table.Column<bool>(type: "INTEGER", nullable: false), Error = table.Column<string>(type: "TEXT", nullable: true), ReportJson = table.Column<string>(type: "TEXT", nullable: true) }, constraints: table =>
        {
            table.PrimaryKey("PK_SmartReads", x => x.DiskId);
            table.ForeignKey(name: "FK_SmartReads_Disks_DiskId", column: x => x.DiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "SmartReads");
}
