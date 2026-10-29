using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class CompletionHelpers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChoreCompletionHelpers",
                columns: table => new
                {
                    ChoreCompletionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HelpersId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreCompletionHelpers", x => new { x.ChoreCompletionId, x.HelpersId });
                    table.ForeignKey(
                        name: "FK_ChoreCompletionHelpers_ChoreCompletions_ChoreCompletionId",
                        column: x => x.ChoreCompletionId,
                        principalTable: "ChoreCompletions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreCompletionHelpers_Users_HelpersId",
                        column: x => x.HelpersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreCompletionHelpers_HelpersId",
                table: "ChoreCompletionHelpers",
                column: "HelpersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreCompletionHelpers");
        }
    }
}
