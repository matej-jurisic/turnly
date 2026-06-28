using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddReassignmentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChoreReassignmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreReassignmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreReassignmentRequests_Chores_ChoreId",
                        column: x => x.ChoreId,
                        principalTable: "Chores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreReassignmentRequests_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChoreReassignmentRequests_Users_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreReassignmentRequests_ChoreId",
                table: "ChoreReassignmentRequests",
                column: "ChoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreReassignmentRequests_FromUserId",
                table: "ChoreReassignmentRequests",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreReassignmentRequests_ToUserId",
                table: "ChoreReassignmentRequests",
                column: "ToUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreReassignmentRequests");
        }
    }
}
