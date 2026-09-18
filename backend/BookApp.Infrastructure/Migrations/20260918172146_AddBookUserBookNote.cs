using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookUserBookNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Books_BookId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_BookId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "BookId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "AddedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Books");

            migrationBuilder.RenameColumn(
                name: "PageNumber",
                table: "Notes",
                newName: "UserBookId");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Books",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "UserBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBooks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserBookId",
                table: "Notes",
                column: "UserBookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_BookId",
                table: "UserBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_BookId",
                table: "UserBooks",
                columns: new[] { "UserId", "BookId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_UserBooks_UserBookId",
                table: "Notes",
                column: "UserBookId",
                principalTable: "UserBooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_UserBooks_UserBookId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "UserBooks");

            migrationBuilder.DropIndex(
                name: "IX_Notes_UserBookId",
                table: "Notes");

            migrationBuilder.RenameColumn(
                name: "UserBookId",
                table: "Notes",
                newName: "PageNumber");

            migrationBuilder.AddColumn<int>(
                name: "BookId",
                table: "Notes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Books",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AddedAt",
                table: "Books",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Books",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_BookId",
                table: "Notes",
                column: "BookId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Books_BookId",
                table: "Notes",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
