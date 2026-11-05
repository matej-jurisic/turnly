using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class NotificationSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DigestSentOn",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DigestTime",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderLeadMinutes",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigestSentOn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DigestTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReminderLeadMinutes",
                table: "Users");
        }
    }
}
