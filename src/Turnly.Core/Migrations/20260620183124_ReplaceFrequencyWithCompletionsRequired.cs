using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFrequencyWithCompletionsRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every chore now carries a per-occurrence completion count; the usual chore needs 1.
            migrationBuilder.AddColumn<int>(
                name: "CompletionsRequired",
                table: "Chores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Fold the old "Custom → Frequency" chores into their equivalent base repeat type:
            // "N times per Week" becomes a Weekly chore that requires N completions per occurrence.
            migrationBuilder.Sql(@"
                UPDATE Chores
                SET RepeatType = CASE FrequencyPeriod
                        WHEN 'Day'   THEN 'Daily'
                        WHEN 'Week'  THEN 'Weekly'
                        WHEN 'Month' THEN 'Monthly'
                        WHEN 'Year'  THEN 'Yearly'
                        ELSE 'Weekly' END,
                    CompletionsRequired = COALESCE(FrequencyCount, 1),
                    CustomMode = NULL
                WHERE CustomMode = 'Frequency';");

            migrationBuilder.DropColumn(
                name: "FrequencyCount",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "FrequencyPeriod",
                table: "Chores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionsRequired",
                table: "Chores");

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
        }
    }
}
