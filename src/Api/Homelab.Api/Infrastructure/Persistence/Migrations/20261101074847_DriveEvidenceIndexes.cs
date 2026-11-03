using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class DriveEvidenceIndexes : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_Observations_DiskId_Metric_ObservedUtc", table: "Observations", columns: new[] { "DiskId", "Metric", "ObservedUtc" });
            migrationBuilder.CreateIndex(name: "IX_Observations_DiskId_Metric_ReceivedUtc", table: "Observations", columns: new[] { "DiskId", "Metric", "ReceivedUtc" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Observations_DiskId_Metric_ObservedUtc", table: "Observations");
            migrationBuilder.DropIndex(name: "IX_Observations_DiskId_Metric_ReceivedUtc", table: "Observations");
        }
    }
}
