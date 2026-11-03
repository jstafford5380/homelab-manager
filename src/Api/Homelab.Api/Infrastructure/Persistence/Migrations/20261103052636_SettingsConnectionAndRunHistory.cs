using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class SettingsConnectionAndRunHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedAttemptUtc",
                table: "ApiPowerOnStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConnectionCredentialHistory",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionCredentialHistory", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_ConnectionCredentialHistory_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionCredentialHistory");

            migrationBuilder.DropColumn(
                name: "LastFailedAttemptUtc",
                table: "ApiPowerOnStates");
        }
    }
}
