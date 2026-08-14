using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class ChoreOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the new table first, copy the old rows into it, and only then drop the old one —
            // EF's default scaffold drops first, which would discard existing requests. SQLite can't
            // widen a NOT NULL column in place, so a copy is the migration either way.
            migrationBuilder.CreateTable(
                name: "ChoreOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BountyPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreOffers_Chores_ChoreId",
                        column: x => x.ChoreId,
                        principalTable: "Chores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreOffers_Users_ClaimedByUserId",
                        column: x => x.ClaimedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChoreOffers_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChoreOffers_Users_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreOffers_ChoreId",
                table: "ChoreOffers",
                column: "ChoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreOffers_ClaimedByUserId",
                table: "ChoreOffers",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreOffers_FromUserId",
                table: "ChoreOffers",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreOffers_ToUserId",
                table: "ChoreOffers",
                column: "ToUserId");

            // Every old request was targeted at one person and carried no bounty. 'Accepted' is now
            // called 'Claimed', and an accepted request's target is by definition who claimed it.
            migrationBuilder.Sql("""
                INSERT INTO ChoreOffers (Id, ChoreId, FromUserId, ToUserId, BountyPoints, ClaimedByUserId, Status, RespondedAt, CreatedAt)
                SELECT Id, ChoreId, FromUserId, ToUserId, 0,
                       CASE WHEN Status = 'Accepted' THEN ToUserId ELSE NULL END,
                       CASE WHEN Status = 'Accepted' THEN 'Claimed' ELSE Status END,
                       RespondedAt, CreatedAt
                FROM ChoreReassignmentRequests;
                """);

            migrationBuilder.DropTable(
                name: "ChoreReassignmentRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChoreReassignmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
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

            // Open offers have no target and can't be represented by the old NOT NULL column, so they
            // are dropped on the way back down. Bounties are lost too — refund before rolling back.
            migrationBuilder.Sql("""
                INSERT INTO ChoreReassignmentRequests (Id, ChoreId, FromUserId, ToUserId, Status, RespondedAt, CreatedAt)
                SELECT Id, ChoreId, FromUserId, ToUserId,
                       CASE WHEN Status IN ('Claimed', 'Withdrawn') THEN
                            CASE WHEN Status = 'Claimed' THEN 'Accepted' ELSE 'Cancelled' END
                       ELSE Status END,
                       RespondedAt, CreatedAt
                FROM ChoreOffers
                WHERE ToUserId IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "ChoreOffers");
        }
    }
}
