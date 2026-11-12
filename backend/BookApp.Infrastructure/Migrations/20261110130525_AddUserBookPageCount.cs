using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBookPageCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "UserBooks",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
DO $$
DECLARE
    invalid_count integer;
BEGIN
    -- 1. Ön kontrol: Books tablosunda geçersiz sayfa sayısı var mı?
    SELECT COUNT(*) INTO invalid_count
    FROM ""Books""
    WHERE ""PageCount"" IS NOT NULL AND (""PageCount"" <= 0 OR ""PageCount"" > 10000);

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'MIGRATION ABORTED: Books tablosunda gecersiz (<= 0 veya > 10000) PageCount degerine sahip % adet kayit bulundu.', invalid_count;
    END IF;

    -- 2. Mevcut UserBook kayıtlarına ilişkili Book.PageCount değerini bir defa kopyala
    UPDATE ""UserBooks"" AS ub
    SET ""PageCount"" = b.""PageCount""
    FROM ""Books"" AS b
    WHERE ub.""BookId"" = b.""Id"";
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "UserBooks");
        }
    }
}
