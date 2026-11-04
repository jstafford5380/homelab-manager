using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedUtc",
                table: "Plans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "Plans",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql("""
                UPDATE Plans SET CompletedUtc = (
                    SELECT AtUtc FROM PlanChanges
                    WHERE PlanId = Plans.Id AND json_extract(AfterJson, '$.Status') = 'complete'
                    AND (BeforeJson IS NULL OR json_extract(BeforeJson, '$.Status') <> 'complete')
                    ORDER BY Id DESC LIMIT 1
                ) WHERE Status = 'complete';
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedUtc",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "Plans");
        }
    }
}
