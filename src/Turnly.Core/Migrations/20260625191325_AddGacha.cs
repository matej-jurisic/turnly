using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGacha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Dust",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EquippedFrameKey",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquippedThemeKey",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PullsSinceLegendary",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "UserCosmetics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CosmeticKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstObtainedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCosmetics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCosmetics_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCosmetics_UserId_CosmeticKey",
                table: "UserCosmetics",
                columns: new[] { "UserId", "CosmeticKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCosmetics");

            migrationBuilder.DropColumn(
                name: "Dust",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EquippedFrameKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EquippedThemeKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PullsSinceLegendary",
                table: "Users");
        }
    }
}
