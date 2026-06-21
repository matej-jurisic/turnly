using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssigneeTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_ChoreNotificationId_OccurrenceDueAt",
                table: "NotificationDeliveries");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "NotificationDeliveries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChoreAssigneeTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletionsRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreAssigneeTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreAssigneeTracks_Chores_ChoreId",
                        column: x => x.ChoreId,
                        principalTable: "Chores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreAssigneeTracks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_ChoreNotificationId_OccurrenceDueAt_UserId",
                table: "NotificationDeliveries",
                columns: new[] { "ChoreNotificationId", "OccurrenceDueAt", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssigneeTracks_ChoreId",
                table: "ChoreAssigneeTracks",
                column: "ChoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssigneeTracks_UserId",
                table: "ChoreAssigneeTracks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreAssigneeTracks");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_ChoreNotificationId_OccurrenceDueAt_UserId",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "NotificationDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_ChoreNotificationId_OccurrenceDueAt",
                table: "NotificationDeliveries",
                columns: new[] { "ChoreNotificationId", "OccurrenceDueAt" },
                unique: true);
        }
    }
}
