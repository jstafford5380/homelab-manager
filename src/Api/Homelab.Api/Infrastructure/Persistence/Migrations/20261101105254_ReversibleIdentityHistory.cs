using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class ReversibleIdentityHistory : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "IdentityMerges", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false), FirstDiskId = table.Column<Guid>(type: "TEXT", nullable: false), SecondDiskId = table.Column<Guid>(type: "TEXT", nullable: false), RequestId = table.Column<Guid>(type: "TEXT", nullable: false), SubmissionHash = table.Column<string>(type: "TEXT", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), ReversedUtc = table.Column<DateTime>(type: "TEXT", nullable: true), ReversalEvidence = table.Column<string>(type: "TEXT", nullable: true), ReversedBy = table.Column<string>(type: "TEXT", nullable: true) }, constraints: table =>
            {
                table.PrimaryKey("PK_IdentityMerges", x => x.Id);
                table.ForeignKey(name: "FK_IdentityMerges_Connections_ConnectionId", column: x => x.ConnectionId, principalTable: "Connections", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_IdentityMerges_Disks_FirstDiskId", column: x => x.FirstDiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_IdentityMerges_Disks_SecondDiskId", column: x => x.SecondDiskId, principalTable: "Disks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_ConnectionId_ReversedUtc", table: "IdentityMerges", columns: new[] { "ConnectionId", "ReversedUtc" });
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_FirstDiskId", table: "IdentityMerges", column: "FirstDiskId");
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_RequestId", table: "IdentityMerges", column: "RequestId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_IdentityMerges_SecondDiskId", table: "IdentityMerges", column: "SecondDiskId");
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IdentityMerges");
        }
    }
}
