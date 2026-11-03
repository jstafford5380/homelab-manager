using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DatasetUsage : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "ReadDatasets", table: "Settings", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "DatasetsJson", table: "Connections", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReadDatasets", table: "Settings");
            migrationBuilder.DropColumn(name: "DatasetsJson", table: "Connections");
        }
    }
}
