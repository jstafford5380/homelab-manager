using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ApiPowerOnHours : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "ReadApiPowerOnHours", table: "Settings", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.CreateTable(name: "ApiPowerOnStates", columns: table => new { ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false), SnapshotJson = table.Column<string>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_ApiPowerOnStates", x => x.ConnectionId);
                table.ForeignKey(name: "FK_ApiPowerOnStates_Connections_ConnectionId", column: x => x.ConnectionId, principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ApiPowerOnStates");
            migrationBuilder.DropColumn(name: "ReadApiPowerOnHours", table: "Settings");
        }
    }
}
