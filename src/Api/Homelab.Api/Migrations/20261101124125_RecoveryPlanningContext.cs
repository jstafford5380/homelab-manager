using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecoveryPlanningContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationCapacity",
                table: "Recovery",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RestoreRequirements",
                table: "Recovery",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationCapacity",
                table: "Recovery");

            migrationBuilder.DropColumn(
                name: "RestoreRequirements",
                table: "Recovery");
        }
    }
}
