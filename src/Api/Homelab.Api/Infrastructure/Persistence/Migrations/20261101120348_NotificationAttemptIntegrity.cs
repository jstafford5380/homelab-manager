using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class NotificationAttemptIntegrity : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse legacy orphans before SQLite temporarily disables foreign keys for rebuild.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__NotificationAttemptGuard" (
                    "MissingDeliveryCount" INTEGER NOT NULL CONSTRAINT "NotificationAttemptRequiresDelivery" CHECK ("MissingDeliveryCount" = 0)
                );
                INSERT INTO "__NotificationAttemptGuard"
                SELECT COUNT(*) FROM "DeliveryAttempts" AS attempt
                WHERE NOT EXISTS (SELECT 1 FROM "Deliveries" AS delivery WHERE delivery."Id" = attempt."DeliveryId");
                DROP TABLE "__NotificationAttemptGuard";
                """);
            migrationBuilder.CreateIndex(name: "IX_DeliveryAttempts_DeliveryId", table: "DeliveryAttempts", column: "DeliveryId");
            migrationBuilder.AddForeignKey(name: "FK_DeliveryAttempts_Deliveries_DeliveryId", table: "DeliveryAttempts", column: "DeliveryId", principalTable: "Deliveries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_DeliveryAttempts_Deliveries_DeliveryId", table: "DeliveryAttempts");
            migrationBuilder.DropIndex(name: "IX_DeliveryAttempts_DeliveryId", table: "DeliveryAttempts");
        }
    }
}
