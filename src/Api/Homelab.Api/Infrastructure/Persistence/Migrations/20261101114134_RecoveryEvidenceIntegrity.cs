using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class RecoveryEvidenceIntegrity : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite table rebuilds temporarily disable FK enforcement. Refuse existing
            // orphans before rebuilding; never delete or reassign operator evidence.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__RecoveryEvidenceIntegrityGuard" (
                    "MissingPolicyCount" INTEGER NOT NULL CONSTRAINT "RecoveryEvidenceRequiresExistingPolicy" CHECK ("MissingPolicyCount" = 0)
                );
                INSERT INTO "__RecoveryEvidenceIntegrityGuard"
                SELECT COUNT(*) FROM "BackupEvidence" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Recovery" AS policy WHERE policy."Id" = evidence."PolicyId");
                INSERT INTO "__RecoveryEvidenceIntegrityGuard"
                SELECT COUNT(*) FROM "RestoreExercises" AS evidence
                WHERE NOT EXISTS (SELECT 1 FROM "Recovery" AS policy WHERE policy."Id" = evidence."PolicyId");
                DROP TABLE "__RecoveryEvidenceIntegrityGuard";
                """);
            migrationBuilder.CreateIndex(name: "IX_RestoreExercises_PolicyId", table: "RestoreExercises", column: "PolicyId");
            migrationBuilder.CreateIndex(name: "IX_BackupEvidence_PolicyId", table: "BackupEvidence", column: "PolicyId");
            migrationBuilder.AddForeignKey(name: "FK_BackupEvidence_Recovery_PolicyId", table: "BackupEvidence", column: "PolicyId", principalTable: "Recovery", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_RestoreExercises_Recovery_PolicyId", table: "RestoreExercises", column: "PolicyId", principalTable: "Recovery", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_BackupEvidence_Recovery_PolicyId", table: "BackupEvidence");
            migrationBuilder.DropForeignKey(name: "FK_RestoreExercises_Recovery_PolicyId", table: "RestoreExercises");
            migrationBuilder.DropIndex(name: "IX_RestoreExercises_PolicyId", table: "RestoreExercises");
            migrationBuilder.DropIndex(name: "IX_BackupEvidence_PolicyId", table: "BackupEvidence");
        }
    }
}
