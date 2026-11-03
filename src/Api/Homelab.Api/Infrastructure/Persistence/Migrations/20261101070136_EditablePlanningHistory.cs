using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class EditablePlanningHistory : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Owner", table: "Plans", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<Guid>(name: "Revision", table: "Plans", type: "TEXT", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            migrationBuilder.CreateTable(name: "PlanChanges", columns: table => new { Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true), PlanId = table.Column<Guid>(type: "TEXT", nullable: false), AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Actor = table.Column<string>(type: "TEXT", nullable: false), BeforeJson = table.Column<string>(type: "TEXT", nullable: true), AfterJson = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_PlanChanges", x => x.Id);
                table.ForeignKey(name: "FK_PlanChanges_Plans_PlanId", column: x => x.PlanId, principalTable: "Plans", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_PlanChanges_PlanId_Id", table: "PlanChanges", columns: new[] { "PlanId", "Id" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlanChanges");
            migrationBuilder.DropColumn(name: "Owner", table: "Plans");
            migrationBuilder.DropColumn(name: "Revision", table: "Plans");
        }
    }
}
