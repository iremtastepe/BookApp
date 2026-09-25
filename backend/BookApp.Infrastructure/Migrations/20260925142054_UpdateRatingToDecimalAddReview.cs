using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRatingToDecimalAddReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Rating",
                table: "UserBooks",
                type: "numeric(2,1)",
                precision: 2,
                scale: 1,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Review",
                table: "UserBooks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Review",
                table: "UserBooks");

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "UserBooks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(2,1)",
                oldPrecision: 2,
                oldScale: 1,
                oldNullable: true);
        }
    }
}
