using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class StableDiscoveryAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoveryIdentityJson",
                table: "Connections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscoveryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationRevision = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionHash = table.Column<string>(type: "TEXT", nullable: false),
                    Wwn = table.Column<string>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevocationEvidence = table.Column<string>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryAssignments_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscoveryAssignments_Disks_DiskId",
                        column: x => x.DiskId,
                        principalTable: "Disks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryAssignments_ConnectionId_Wwn_RevokedUtc",
                table: "DiscoveryAssignments",
                columns: new[] { "ConnectionId", "Wwn", "RevokedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryAssignments_DiskId",
                table: "DiscoveryAssignments",
                column: "DiskId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryAssignments_RequestId",
                table: "DiscoveryAssignments",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryAssignments");

            migrationBuilder.DropColumn(
                name: "DiscoveryIdentityJson",
                table: "Connections");
        }
    }
}
