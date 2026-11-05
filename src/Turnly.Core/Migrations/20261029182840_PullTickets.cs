using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class PullTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PullTickets",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TicketProgress",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Seed tickets from the balance people hold today (one per PointsPerTicket = 50), with
            // the remainder as progress toward the next. Their points are kept as they are.
            migrationBuilder.Sql(
                "UPDATE Users SET PullTickets = MAX(Points, 0) / 50, TicketProgress = MAX(Points, 0) % 50;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PullTickets",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TicketProgress",
                table: "Users");
        }
    }
}
