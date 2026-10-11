using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingGoalsAndFinishedDateComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "UserBooks");

            migrationBuilder.AddColumn<int>(
                name: "FinishedDay",
                table: "UserBooks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinishedMonth",
                table: "UserBooks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinishedYear",
                table: "UserBooks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TargetBooks = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingGoals", x => x.Id);
                    table.CheckConstraint("CK_ReadingGoals_TargetBooks_Positive", "\"TargetBooks\" > 0");
                    table.CheckConstraint("CK_ReadingGoals_Year_Range", "\"Year\" >= 1 AND \"Year\" <= 9999");
                    table.ForeignKey(
                        name: "FK_ReadingGoals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_Status_FinishedYear",
                table: "UserBooks",
                columns: new[] { "UserId", "Status", "FinishedYear" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_Status_FinishedYear_FinishedMonth",
                table: "UserBooks",
                columns: new[] { "UserId", "Status", "FinishedYear", "FinishedMonth" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserBooks_FinishedDate_Hierarchy",
                table: "UserBooks",
                sql: "(\"FinishedYear\" IS NULL AND \"FinishedMonth\" IS NULL AND \"FinishedDay\" IS NULL) OR (\"FinishedYear\" IS NOT NULL AND \"FinishedMonth\" IS NULL AND \"FinishedDay\" IS NULL) OR (\"FinishedYear\" IS NOT NULL AND \"FinishedMonth\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserBooks_FinishedDay_CalendarValid",
                table: "UserBooks",
                sql: "\"FinishedDay\" IS NULL OR (\"FinishedDay\" >= 1 AND ((\"FinishedMonth\" IN (1, 3, 5, 7, 8, 10, 12) AND \"FinishedDay\" <= 31) OR (\"FinishedMonth\" IN (4, 6, 9, 11) AND \"FinishedDay\" <= 30) OR (\"FinishedMonth\" = 2 AND ((((\"FinishedYear\" % 4 = 0 AND \"FinishedYear\" % 100 <> 0) OR (\"FinishedYear\" % 400 = 0)) AND \"FinishedDay\" <= 29) OR ((NOT ((\"FinishedYear\" % 4 = 0 AND \"FinishedYear\" % 100 <> 0) OR (\"FinishedYear\" % 400 = 0))) AND \"FinishedDay\" <= 28)))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserBooks_FinishedMonth_Range",
                table: "UserBooks",
                sql: "\"FinishedMonth\" IS NULL OR (\"FinishedMonth\" >= 1 AND \"FinishedMonth\" <= 12)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserBooks_FinishedYear_Range",
                table: "UserBooks",
                sql: "\"FinishedYear\" IS NULL OR (\"FinishedYear\" >= 1 AND \"FinishedYear\" <= 9999)");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingGoals_UserId_Year",
                table: "ReadingGoals",
                columns: new[] { "UserId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingGoals");

            migrationBuilder.DropIndex(
                name: "IX_UserBooks_UserId_Status_FinishedYear",
                table: "UserBooks");

            migrationBuilder.DropIndex(
                name: "IX_UserBooks_UserId_Status_FinishedYear_FinishedMonth",
                table: "UserBooks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserBooks_FinishedDate_Hierarchy",
                table: "UserBooks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserBooks_FinishedDay_CalendarValid",
                table: "UserBooks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserBooks_FinishedMonth_Range",
                table: "UserBooks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserBooks_FinishedYear_Range",
                table: "UserBooks");

            migrationBuilder.DropColumn(
                name: "FinishedDay",
                table: "UserBooks");

            migrationBuilder.DropColumn(
                name: "FinishedMonth",
                table: "UserBooks");

            migrationBuilder.DropColumn(
                name: "FinishedYear",
                table: "UserBooks");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "UserBooks",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
