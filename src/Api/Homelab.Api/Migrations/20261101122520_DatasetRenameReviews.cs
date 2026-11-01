using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Migrations
{
    /// <inheritdoc />
    public partial class DatasetRenameReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expose the composite parent key before SQLite creates/copies dependent tables.
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_DatasetRenameUpgradeKey\" ON \"DatasetSamples\" (\"Id\", \"ConnectionId\");");
            migrationBuilder.AddUniqueConstraint(
                name: "AK_DatasetSamples_Id_ConnectionId",
                table: "DatasetSamples",
                columns: new[] { "Id", "ConnectionId" });

            migrationBuilder.CreateTable(
                name: "DatasetRenames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromSampleId = table.Column<long>(type: "INTEGER", nullable: false),
                    ToSampleId = table.Column<long>(type: "INTEGER", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", nullable: false),
                    ToName = table.Column<string>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionHash = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReversedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReversalEvidence = table.Column<string>(type: "TEXT", nullable: true),
                    ReversedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetRenames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetRenames_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatasetRenames_DatasetSamples_FromSampleId_ConnectionId",
                        columns: x => new { x.FromSampleId, x.ConnectionId },
                        principalTable: "DatasetSamples",
                        principalColumns: new[] { "Id", "ConnectionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatasetRenames_DatasetSamples_ToSampleId_ConnectionId",
                        columns: x => new { x.ToSampleId, x.ConnectionId },
                        principalTable: "DatasetSamples",
                        principalColumns: new[] { "Id", "ConnectionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRenames_ConnectionId_Id",
                table: "DatasetRenames",
                columns: new[] { "ConnectionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRenames_FromSampleId_ConnectionId",
                table: "DatasetRenames",
                columns: new[] { "FromSampleId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRenames_RequestId",
                table: "DatasetRenames",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRenames_ToSampleId_ConnectionId",
                table: "DatasetRenames",
                columns: new[] { "ToSampleId", "ConnectionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetRenames");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DatasetSamples_Id_ConnectionId",
                table: "DatasetSamples");
        }
    }
}
