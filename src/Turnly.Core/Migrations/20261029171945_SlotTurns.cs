using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class SlotTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TurnPerSlot",
                table: "Chores",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "SlotTime",
                table: "ChoreOffers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "SlotTime",
                table: "ChoreAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChoreSlotTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CurrentAssigneeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentPartnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreSlotTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreSlotTurns_Chores_ChoreId",
                        column: x => x.ChoreId,
                        principalTable: "Chores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreSlotTurns_Users_CurrentAssigneeId",
                        column: x => x.CurrentAssigneeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreSlotTurns_ChoreId_Time",
                table: "ChoreSlotTurns",
                columns: new[] { "ChoreId", "Time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreSlotTurns_CurrentAssigneeId",
                table: "ChoreSlotTurns",
                column: "CurrentAssigneeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreSlotTurns");

            migrationBuilder.DropColumn(
                name: "TurnPerSlot",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "SlotTime",
                table: "ChoreOffers");

            migrationBuilder.DropColumn(
                name: "SlotTime",
                table: "ChoreAssignments");
        }
    }
}
