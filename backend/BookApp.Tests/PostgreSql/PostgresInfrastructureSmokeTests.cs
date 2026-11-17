
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookApp.Tests.PostgreSql;

// 7.4-C.1: Gerçek PostgreSQL üzerinde başlangıç testleri.
[Collection(PostgresCollection.Name)]
public class PostgresInfrastructureSmokeTests
{
    private readonly PostgresDatabaseFixture _db;

    public PostgresInfrastructureSmokeTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static async Task<(User User, Book Book)> SeedUserAndBookAsync(
        AppDbContext ctx)
    {
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            UserName = $"pgtest_{tag}",
            NormalizedUserName = $"PGTEST_{tag}".ToUpperInvariant(),
            Email = $"pgtest_{tag}@test.local",
            NormalizedEmail = $"PGTEST_{tag}@TEST.LOCAL".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var book = new Book
        {
            Title = $"PG Book {tag}",
            Author = "PG Author"
        };

        ctx.Users.Add(user);
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();

        return (user, book);
    }

    private static Task<long> ReadXminAsync(AppDbContext ctx, int userBookId)
        => ctx.Database
            .SqlQuery<long>(
                $"SELECT xmin::text::bigint AS \"Value\" FROM \"UserBooks\" WHERE \"Id\" = {userBookId}")
            .SingleAsync();

    // 1. Doğru test veritabanına bağlanılıyor mu?
    [PostgresFact]
    public async Task Connection_TargetsOnlyTheTestDatabase()
    {
        await using var ctx = _db.CreateContext();

        var name = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT current_database() AS \"Value\"")
            .SingleAsync();

        Assert.Equal(PostgresTestSettings.RequiredDatabaseName, name);
        Assert.NotEqual("BookAppDb", name);
    }

    // 2. Migration zinciri uygulanmış mı, bekleyen migration var mı?
    [PostgresFact]
    public async Task Migrations_AreApplied_AndNoneArePending()
    {
        await using var ctx = _db.CreateContext();

        Assert.NotEmpty(_db.AppliedMigrations);

        Assert.Equal(
            ctx.Database.GetMigrations().ToList(),
            _db.AppliedMigrations);

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());

        Assert.Contains(
            _db.AppliedMigrations,
            m => m.EndsWith(
                "_AddUserBookPageCount",
                StringComparison.Ordinal));
    }

    // 3. Gerekli tablolar ve sütunlar mevcut mu?
    [PostgresFact]
    public async Task Schema_HasExpectedTablesAndColumns_AndXminIsNotAPhysicalColumn()
    {
        await using var ctx = _db.CreateContext();

        var tables = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")
            .ToListAsync();

        foreach (var expected in new[]
        {
            "Users", "Roles", "UserRoles", "Books",
            "UserBooks", "Notes", "ReadingGoals",
            "__EFMigrationsHistory"
        })
        {
            Assert.Contains(expected, tables);
        }

        var columns = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = 'UserBooks'")
            .ToListAsync();

        foreach (var expected in new[]
        {
            "UserId", "BookId", "Status",
            "PageCount", "CurrentPage", "ArchivedAt"
        })
        {
            Assert.Contains(expected, columns);
        }

        Assert.DoesNotContain("xmin", columns);
    }

    // 4. Aynı kullanıcı ve kitap için UNIQUE index var mı?
    [PostgresFact]
    public async Task UniqueIndex_OnUserIdAndBookId_Exists()
    {
        await using var ctx = _db.CreateContext();

        var definition = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes " +
                "WHERE schemaname = 'public' AND tablename = 'UserBooks' " +
                "AND indexname = 'IX_UserBooks_UserId_BookId'")
            .SingleOrDefaultAsync();

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition);
    }

    // 5. Aynı kitap ikinci kez eklenince 23505 hatası oluşuyor mu?
    [PostgresFact]
    public async Task DuplicateUserBook_RaisesUniqueViolation_23505()
    {
        await using var ctx = _db.CreateContext();

        var (user, book) = await SeedUserAndBookAsync(ctx);

        ctx.UserBooks.Add(new UserBook
        {
            UserId = user.Id,
            BookId = book.Id
        });

        await ctx.SaveChangesAsync();

        await using var ctx2 = _db.CreateContext();

        ctx2.UserBooks.Add(new UserBook
        {
            UserId = user.Id,
            BookId = book.Id
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => ctx2.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);

        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("IX_UserBooks_UserId_BookId", pg.ConstraintName);
    }

    // 6. xmin değeri güncelleme sonrası değişiyor mu?
    [PostgresFact]
    public async Task Xmin_IsReadable_AndChangesAfterUpdate()
    {
        int userBookId;
        long before;

        await using (var ctx = _db.CreateContext())
        {
            var (user, book) = await SeedUserAndBookAsync(ctx);

            var userBook = new UserBook
            {
                UserId = user.Id,
                BookId = book.Id,
                PageCount = 100
            };

            ctx.UserBooks.Add(userBook);
            await ctx.SaveChangesAsync();

            userBookId = userBook.Id;
            before = await ReadXminAsync(ctx, userBookId);
        }

        Assert.True(before > 0);

        await using (var ctx = _db.CreateContext())
        {
            var tracked = await ctx.UserBooks
                .SingleAsync(x => x.Id == userBookId);

            tracked.CurrentPage = 10;

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.CreateContext())
        {
            var after = await ReadXminAsync(ctx, userBookId);

            Assert.NotEqual(before, after);
        }
    }

    // 7. Türkçe ICU collation mevcut mu?
    [PostgresFact]
    public async Task TurkishIcuCollation_IsAvailable()
    {
        await using var ctx = _db.CreateContext();

        var count = await ctx.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_collation " +
                "WHERE collname = 'tr-x-icu'")
            .SingleAsync();

        Assert.True(
            count >= 1,
            "'tr-x-icu' harmanlaması bulunamadı.");
    }

    // 8. Kitaplar Türk alfabesine göre sıralanıyor mu?
    [PostgresFact]
    public async Task Books_SortedWithTurkishIcu_FollowTurkishAlphabet()
    {
        var titles = new[]
        {
            "Zeytin", "Üzüm", "Ulaş", "Şeker",
            "Sarı", "Ömer", "Oba", "İnci",
            "Ilık", "Dal", "Çilek", "Cam"
        };

        await using var ctx = _db.CreateContext();

        var books = titles
            .Select(t => new Book
            {
                Title = t,
                Author = "PG Collation"
            })
            .ToList();

        ctx.Books.AddRange(books);
        await ctx.SaveChangesAsync();

        var ids = books.Select(b => b.Id).ToList();

        var sorted = await ctx.Books
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => EF.Functions.Collate(b.Title, "tr-x-icu"))
            .Select(b => b.Title)
            .ToListAsync();

        var expected = new[]
        {
            "Cam", "Çilek", "Dal", "Ilık",
            "İnci", "Oba", "Ömer", "Sarı",
            "Şeker", "Ulaş", "Üzüm", "Zeytin"
        };

        Assert.Equal(expected, sorted);
    }
}
