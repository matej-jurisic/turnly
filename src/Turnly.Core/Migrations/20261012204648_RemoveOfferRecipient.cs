using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Turnly.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOfferRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreOffers_Users_ToUserId",
                table: "ChoreOffers");

            migrationBuilder.DropIndex(
                name: "IX_ChoreOffers_ToUserId",
                table: "ChoreOffers");

            migrationBuilder.DropColumn(
                name: "ToUserId",
                table: "ChoreOffers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ToUserId",
                table: "ChoreOffers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreOffers_ToUserId",
                table: "ChoreOffers",
                column: "ToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreOffers_Users_ToUserId",
                table: "ChoreOffers",
                column: "ToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
