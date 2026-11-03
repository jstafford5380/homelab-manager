using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class PoolCapacityTopology : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "TopologySignature", table: "PoolSnapshots", type: "TEXT", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_PoolSnapshots_ConnectionId_PoolGuid_FetchedUtc", table: "PoolSnapshots", columns: new[] { "ConnectionId", "PoolGuid", "FetchedUtc" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_PoolSnapshots_ConnectionId_PoolGuid_FetchedUtc", table: "PoolSnapshots");
            migrationBuilder.DropColumn(name: "TopologySignature", table: "PoolSnapshots");
        }
    }
}
