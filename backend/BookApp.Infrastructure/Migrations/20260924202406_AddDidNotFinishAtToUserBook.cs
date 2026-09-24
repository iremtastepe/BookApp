using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDidNotFinishAtToUserBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DidNotFinishAt",
                table: "UserBooks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DidNotFinishAt",
                table: "UserBooks");
        }
    }
}
