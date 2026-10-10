using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddChoreRotationPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RotationPeriod",
                table: "Chores",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                // Existing chores keep rotating every occurrence; an empty string would not parse
                // back into the enum.
                defaultValue: "EveryOccurrence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RotationPeriod",
                table: "Chores");
        }
    }
}
