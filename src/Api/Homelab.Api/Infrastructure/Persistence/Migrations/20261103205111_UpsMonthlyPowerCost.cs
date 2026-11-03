using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpsMonthlyPowerCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ElectricityRateUsdPerKwh",
                table: "Settings",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UpsPowerSamples",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceKey = table.Column<string>(type: "TEXT", nullable: false),
                    BucketUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LoadPercent = table.Column<double>(type: "REAL", nullable: false),
                    NominalVa = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpsPowerSamples", x => new { x.ServerId, x.DeviceKey, x.BucketUtc });
                    table.ForeignKey(
                        name: "FK_UpsPowerSamples_HardwareServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "HardwareServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UpsPowerSamples_BucketUtc",
                table: "UpsPowerSamples",
                column: "BucketUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpsPowerSamples");

            migrationBuilder.DropColumn(
                name: "ElectricityRateUsdPerKwh",
                table: "Settings");
        }
    }
}
