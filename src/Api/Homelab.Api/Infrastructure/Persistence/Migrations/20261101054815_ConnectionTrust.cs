using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ConnectionTrust : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "CertificateSha256", table: "Connections", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ConfigurationRevision", table: "Connections", type: "TEXT", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            migrationBuilder.AddColumn<string>(name: "TrustedCaPem", table: "Connections", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CertificateSha256", table: "Connections");
            migrationBuilder.DropColumn(name: "ConfigurationRevision", table: "Connections");
            migrationBuilder.DropColumn(name: "TrustedCaPem", table: "Connections");
        }
    }
}
