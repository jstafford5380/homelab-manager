using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class NasSystemIdentity : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ObservedSystemSerial", table: "Connections", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SystemIdentityCheckedUtc", table: "Connections", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "SystemIdentityState", table: "Connections", type: "TEXT", nullable: false, defaultValue: "unavailable");
            migrationBuilder.AddColumn<string>(name: "SystemSerial", table: "Connections", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ObservedSystemSerial", table: "Connections");
            migrationBuilder.DropColumn(name: "SystemIdentityCheckedUtc", table: "Connections");
            migrationBuilder.DropColumn(name: "SystemIdentityState", table: "Connections");
            migrationBuilder.DropColumn(name: "SystemSerial", table: "Connections");
        }
    }
}
