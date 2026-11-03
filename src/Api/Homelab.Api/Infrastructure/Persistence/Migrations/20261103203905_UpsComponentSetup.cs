using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpsComponentSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpsDeviceKey",
                table: "HardwareComponents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "UpsNominalVa",
                table: "HardwareComponents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "UpsPowerFactor",
                table: "HardwareComponents",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpsDeviceKey",
                table: "HardwareComponents");

            migrationBuilder.DropColumn(
                name: "UpsNominalVa",
                table: "HardwareComponents");

            migrationBuilder.DropColumn(
                name: "UpsPowerFactor",
                table: "HardwareComponents");
        }
    }
}
