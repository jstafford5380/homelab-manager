using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class VdevSnapshotEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VdevEvidenceJson",
                table: "PoolSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VdevEvidenceJson",
                table: "PoolDailySummaries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VdevEvidenceJson",
                table: "PoolSnapshots");

            migrationBuilder.DropColumn(
                name: "VdevEvidenceJson",
                table: "PoolDailySummaries");
        }
    }
}
