using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class PersonalNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderLeadMinutes",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ChoreNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreNotifications_UserId",
                table: "ChoreNotifications",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreNotifications_Users_UserId",
                table: "ChoreNotifications",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreNotifications_Users_UserId",
                table: "ChoreNotifications");

            migrationBuilder.DropIndex(
                name: "IX_ChoreNotifications_UserId",
                table: "ChoreNotifications");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ChoreNotifications");

            migrationBuilder.AddColumn<int>(
                name: "ReminderLeadMinutes",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }
    }
}
