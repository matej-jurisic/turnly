using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoAdvanceIncomplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAdvanceIncomplete",
                table: "Chores",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CompletionWindowMinutes",
                table: "Chores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompletedByUserId",
                table: "ChoreCompletions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "IsExpired",
                table: "ChoreCompletions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoAdvanceIncomplete",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "CompletionWindowMinutes",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "IsExpired",
                table: "ChoreCompletions");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompletedByUserId",
                table: "ChoreCompletions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
