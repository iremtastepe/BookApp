using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookCreatorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedSource",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_CreatedByUserId",
                table: "Books",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Users_CreatedByUserId",
                table: "Books",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Users_CreatedByUserId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_CreatedByUserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CreatedSource",
                table: "Books");
        }
    }
}
