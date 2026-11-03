using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class PlanningReviewSettings : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "DriveAgeReviewYears", table: "Settings", type: "INTEGER", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(name: "WarrantyNoticeDays", table: "Settings", type: "INTEGER", nullable: false, defaultValue: 90);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DriveAgeReviewYears", table: "Settings");
            migrationBuilder.DropColumn(name: "WarrantyNoticeDays", table: "Settings");
        }
    }
}
