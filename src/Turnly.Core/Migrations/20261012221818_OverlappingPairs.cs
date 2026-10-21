using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class OverlappingPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentPartnerId",
                table: "Chores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousPartnerId",
                table: "ChoreCompletions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerUserId",
                table: "ChoreAssignments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentPartnerId",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "PreviousPartnerId",
                table: "ChoreCompletions");

            migrationBuilder.DropColumn(
                name: "PartnerUserId",
                table: "ChoreAssignments");
        }
    }
}
