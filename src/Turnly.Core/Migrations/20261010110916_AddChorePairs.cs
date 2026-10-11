using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddChorePairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pairs",
                table: "Chores",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerUserId",
                table: "ChoreCompletions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreCompletions_PartnerUserId",
                table: "ChoreCompletions",
                column: "PartnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreCompletions_Users_PartnerUserId",
                table: "ChoreCompletions",
                column: "PartnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreCompletions_Users_PartnerUserId",
                table: "ChoreCompletions");

            migrationBuilder.DropIndex(
                name: "IX_ChoreCompletions_PartnerUserId",
                table: "ChoreCompletions");

            migrationBuilder.DropColumn(
                name: "Pairs",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "PartnerUserId",
                table: "ChoreCompletions");
        }
    }
}
