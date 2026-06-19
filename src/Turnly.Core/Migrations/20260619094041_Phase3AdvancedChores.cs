using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase3AdvancedChores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignmentStrategy",
                table: "Chores",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "KeepLastAssigned");

            migrationBuilder.AddColumn<string>(
                name: "CustomMode",
                table: "Chores",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DaysOfMonth",
                table: "Chores",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FrequencyCount",
                table: "Chores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrequencyPeriod",
                table: "Chores",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntervalCount",
                table: "Chores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntervalUnit",
                table: "Chores",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Months",
                table: "Chores",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SchedulingPreference",
                table: "Chores",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "FromScheduledDate");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousAssigneeId",
                table: "ChoreCompletions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChoreAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ChoreCompletionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreAssignments_Chores_ChoreId",
                        column: x => x.ChoreId,
                        principalTable: "Chores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssignments_ChoreId",
                table: "ChoreAssignments",
                column: "ChoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssignments_UserId",
                table: "ChoreAssignments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreAssignments");

            migrationBuilder.DropColumn(
                name: "AssignmentStrategy",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "CustomMode",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "DaysOfMonth",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "FrequencyCount",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "FrequencyPeriod",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "IntervalCount",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "IntervalUnit",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "Months",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "SchedulingPreference",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "PreviousAssigneeId",
                table: "ChoreCompletions");
        }
    }
}
