using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class PoolHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoolSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PoolGuid = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceSchema = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    AllocatedBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    FreeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ScanFunction = table.Column<string>(type: "TEXT", nullable: true),
                    ScanState = table.Column<string>(type: "TEXT", nullable: true),
                    ScanPercentage = table.Column<double>(type: "REAL", nullable: true),
                    ScanErrors = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoolSnapshots_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoolSnapshots_ConnectionId_PoolGuid_Id",
                table: "PoolSnapshots",
                columns: new[] { "ConnectionId", "PoolGuid", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolSnapshots");
        }
    }
}
