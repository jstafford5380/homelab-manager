using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class RecoveryEvidence : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Exclusions", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "FailureDomains", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Importance", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Independence", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "KeyCustodyReference", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Owner", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Retention", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<Guid>(name: "Revision", table: "Recovery", type: "TEXT", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            migrationBuilder.AddColumn<int>(name: "RtoHours", table: "Recovery", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "Runbook", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Schedule", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Scope", table: "Recovery", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.CreateTable(name: "BackupEvidence", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), PolicyId = table.Column<Guid>(type: "TEXT", nullable: false), PolicyRevision = table.Column<Guid>(type: "TEXT", nullable: false), AttemptedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Outcome = table.Column<string>(type: "TEXT", nullable: false), RecoveryPointUtc = table.Column<DateTime>(type: "TEXT", nullable: true), DestinationConfirmed = table.Column<bool>(type: "INTEGER", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), FailureReason = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), ReviewByUtc = table.Column<DateTime>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_BackupEvidence", x => x.Id);
            });
            migrationBuilder.CreateTable(name: "RestoreExercises", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), PolicyId = table.Column<Guid>(type: "TEXT", nullable: false), PolicyRevision = table.Column<Guid>(type: "TEXT", nullable: false), TestedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), RecoveryPointUtc = table.Column<DateTime>(type: "TEXT", nullable: false), Scope = table.Column<string>(type: "TEXT", nullable: false), Checks = table.Column<string>(type: "TEXT", nullable: false), Result = table.Column<string>(type: "TEXT", nullable: false), DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false), Evidence = table.Column<string>(type: "TEXT", nullable: false), Author = table.Column<string>(type: "TEXT", nullable: false), RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false), ReviewByUtc = table.Column<DateTime>(type: "TEXT", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_RestoreExercises", x => x.Id);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BackupEvidence");
            migrationBuilder.DropTable(name: "RestoreExercises");
            migrationBuilder.DropColumn(name: "Exclusions", table: "Recovery");
            migrationBuilder.DropColumn(name: "FailureDomains", table: "Recovery");
            migrationBuilder.DropColumn(name: "Importance", table: "Recovery");
            migrationBuilder.DropColumn(name: "Independence", table: "Recovery");
            migrationBuilder.DropColumn(name: "KeyCustodyReference", table: "Recovery");
            migrationBuilder.DropColumn(name: "Owner", table: "Recovery");
            migrationBuilder.DropColumn(name: "Retention", table: "Recovery");
            migrationBuilder.DropColumn(name: "Revision", table: "Recovery");
            migrationBuilder.DropColumn(name: "RtoHours", table: "Recovery");
            migrationBuilder.DropColumn(name: "Runbook", table: "Recovery");
            migrationBuilder.DropColumn(name: "Schedule", table: "Recovery");
            migrationBuilder.DropColumn(name: "Scope", table: "Recovery");
        }
    }
}
