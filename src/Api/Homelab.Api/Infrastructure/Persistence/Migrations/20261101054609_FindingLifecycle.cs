using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class FindingLifecycle : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ResetEpoch", table: "Observations", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "SourceSchema", table: "Observations", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "AssignedOwner", table: "Findings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "EvidenceObservedUtc", table: "Findings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "EvidenceSource", table: "Findings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>(name: "HealthySamples", table: "Findings", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ReminderSequence", table: "Findings", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "ResolutionEvidence", table: "Findings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ResolvedUtc", table: "Findings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SnoozedUntilUtc", table: "Findings", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ResetEpoch", table: "Observations");
            migrationBuilder.DropColumn(name: "SourceSchema", table: "Observations");
            migrationBuilder.DropColumn(name: "AssignedOwner", table: "Findings");
            migrationBuilder.DropColumn(name: "EvidenceObservedUtc", table: "Findings");
            migrationBuilder.DropColumn(name: "EvidenceSource", table: "Findings");
            migrationBuilder.DropColumn(name: "HealthySamples", table: "Findings");
            migrationBuilder.DropColumn(name: "ReminderSequence", table: "Findings");
            migrationBuilder.DropColumn(name: "ResolutionEvidence", table: "Findings");
            migrationBuilder.DropColumn(name: "ResolvedUtc", table: "Findings");
            migrationBuilder.DropColumn(name: "SnoozedUntilUtc", table: "Findings");
        }
    }
}
