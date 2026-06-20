using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class ReassignmentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByUserId",
                table: "ChoreAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousAssigneeId",
                table: "ChoreAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssignments_AssignedByUserId",
                table: "ChoreAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreAssignments_PreviousAssigneeId",
                table: "ChoreAssignments",
                column: "PreviousAssigneeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreAssignments_Users_AssignedByUserId",
                table: "ChoreAssignments",
                column: "AssignedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreAssignments_Users_PreviousAssigneeId",
                table: "ChoreAssignments",
                column: "PreviousAssigneeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreAssignments_Users_AssignedByUserId",
                table: "ChoreAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ChoreAssignments_Users_PreviousAssigneeId",
                table: "ChoreAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChoreAssignments_AssignedByUserId",
                table: "ChoreAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChoreAssignments_PreviousAssigneeId",
                table: "ChoreAssignments");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "ChoreAssignments");

            migrationBuilder.DropColumn(
                name: "PreviousAssigneeId",
                table: "ChoreAssignments");
        }
    }
}
