using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeBookDeleteBehaviorToRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserBooks_Books_BookId",
                table: "UserBooks");

            migrationBuilder.AddForeignKey(
                name: "FK_UserBooks_Books_BookId",
                table: "UserBooks",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserBooks_Books_BookId",
                table: "UserBooks");

            migrationBuilder.AddForeignKey(
                name: "FK_UserBooks_Books_BookId",
                table: "UserBooks",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
