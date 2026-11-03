using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Homelab.Api.Migrations
{
    /// <inheritdoc/>
    public partial class StorageDriveProfile : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BayConfirmedIdentity", table: "Disks", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "DriveType", table: "Disks", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>(name: "Enclosure", table: "Disks", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Nickname", table: "Disks", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>(name: "RotationRate", table: "Disks", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<long>(name: "SizeBytes", table: "Disks", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<int>(name: "Slot", table: "Disks", type: "INTEGER", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BayConfirmedIdentity", table: "Disks");
            migrationBuilder.DropColumn(name: "DriveType", table: "Disks");
            migrationBuilder.DropColumn(name: "Enclosure", table: "Disks");
            migrationBuilder.DropColumn(name: "Nickname", table: "Disks");
            migrationBuilder.DropColumn(name: "RotationRate", table: "Disks");
            migrationBuilder.DropColumn(name: "SizeBytes", table: "Disks");
            migrationBuilder.DropColumn(name: "Slot", table: "Disks");
        }
    }
}
