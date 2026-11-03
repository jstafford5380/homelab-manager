using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class HardwareReviewDefaults : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair only the invalid all-zero tuple assigned to existing Settings rows by 002's original migration.
            migrationBuilder.Sql("UPDATE Settings SET HardwareCpuPercent=80, HardwareMemoryPercent=80, HardwareTemperatureC=80, HardwareSustainedMinutes=60 WHERE HardwareCpuPercent=0 AND HardwareMemoryPercent=0 AND HardwareTemperatureC=0 AND HardwareSustainedMinutes=0");
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
