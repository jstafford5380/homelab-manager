using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homelab.Api.Infrastructure.Persistence.Migrations;

public partial class ApiKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ApiKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Access = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                SecretHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastUsedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApiKeys", key => key.Id);
                table.ForeignKey("FK_ApiKeys_Owners_OwnerId", key => key.OwnerId, "Owners", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ApiKeys_OwnerId", "ApiKeys", "OwnerId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ApiKeys");
}
