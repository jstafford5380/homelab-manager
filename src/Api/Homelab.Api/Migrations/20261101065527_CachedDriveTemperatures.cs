using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class CachedDriveTemperatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReadCachedTemperatures",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CachedTemperaturesJson",
                table: "Connections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadCachedTemperatures",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CachedTemperaturesJson",
                table: "Connections");
        }
    }
}
