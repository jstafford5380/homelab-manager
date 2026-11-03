using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class LocalTemperaturePolicy : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "TemperaturePolicyReason", table: "Settings", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<double>(name: "TemperatureReviewCelsius", table: "Settings", type: "REAL", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TemperaturePolicyReason", table: "Settings");
            migrationBuilder.DropColumn(name: "TemperatureReviewCelsius", table: "Settings");
        }
    }
}
