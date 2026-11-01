using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class DatasetInventoryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatasetSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationRevision = table.Column<Guid>(type: "TEXT", nullable: false),
                    HourUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SystemSerial = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetSamples_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetSamples_ConnectionId_ConfigurationRevision_HourUtc",
                table: "DatasetSamples",
                columns: new[] { "ConnectionId", "ConfigurationRevision", "HourUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatasetSamples_ConnectionId_Id",
                table: "DatasetSamples",
                columns: new[] { "ConnectionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetSamples_FetchedUtc",
                table: "DatasetSamples",
                column: "FetchedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetSamples");
        }
    }
}
