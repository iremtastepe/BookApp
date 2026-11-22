
using BookApp.Application.DTOs.Library;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookApp.Tests.PostgreSql;

// 7.4-C.2 (R5): Gerçek PostgreSQL üzerinde eşzamanlı kitap ekleme.
// Yalnızca BookAppDb_Test kullanılır. Testler mevcut verileri silmez.
[Collection(PostgresCollection.Name)]
public class PostgresConcurrentAddToLibraryTests
{
    private const string UniqueIndexName = "IX_UserBooks_UserId_BookId";
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(20);

    private readonly PostgresDatabaseFixture _db;

    public PostgresConcurrentAddToLibraryTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private sealed record AddOutcome(UserBookDto? Result, Exception? Error);

    // Her test için benzersiz kullanıcı oluşturur.
    private async Task<int> SeedUserAsync()
    {
        var tag = Guid.NewGuid().ToString("N");
        await using var ctx = _db.CreateContext();

        var user = new User
        {
            UserName = $"pgr5_{tag}",
            NormalizedUserName = $"PGR5_{tag}".ToUpperInvariant(),
            Email = $"pgr5_{tag}@test.local",
            NormalizedEmail = $"PGR5_{tag}@TEST.LOCAL".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    // Her test için benzersiz kitap oluşturur.
    private async Task<int> SeedBookAsync(int? pageCount = null)
    {
        var tag = Guid.NewGuid().ToString("N");
        await using var ctx = _db.CreateContext();

        var book = new Book
        {
            Title = $"PG R5 Book {tag}",
            Author = "PG R5 Author",
            PageCount = pageCount
        };

        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        return book.Id;
    }

    private async Task<int> CountUserBooksAsync(int userId, int bookId)
    {
        await using var ctx = _db.CreateContext();

        return await ctx.UserBooks.CountAsync(
            ub => ub.UserId == userId && ub.BookId == bookId);
    }

    // Her istek ayrı DbContext kullanır.
    private async Task<AddOutcome> RunAddAsync(
        int userId, int bookId, RaceGate? gate)
    {
        await using var ctx = _db.CreateContext();

        IUserBookRepository userBooks = new UserBookRepository(ctx);

        if (gate is not null)
        {
            userBooks = new RaceGateUserBookRepository(
                userBooks, gate, GateTimeout);
        }

        var service = new UserBookService(
            userBooks, new BookRepository(ctx));

        try
        {
            var dto = await service.AddToLibraryAsync(
                userId,
                new AddToLibraryRequest { BookId = bookId });

            return new AddOutcome(dto, null);
        }
        catch (Exception ex)
        {
            return new AddOutcome(null, ex);
        }
    }

    private static string Describe(IEnumerable<AddOutcome> outcomes)
        => string.Join(" | ", outcomes.Select(
            o => o.Error is null
                ? "Başarılı"
                : o.Error.GetType().Name));

    // Çakışmanın gerçek PostgreSQL 23505 hatasından geldiğini doğrular.
    private static void AssertIsUserBookUniqueViolation(Exception loser)
    {
        Assert.Equal(
            typeof(UserBookAlreadyExistsException),
            loser.GetType());

        Assert.Equal(
            UserBookAlreadyExistsException.DefaultMessage,
            loser.Message);

        var dbEx = Assert.IsType<DbUpdateException>(
            loser.InnerException);

        var pg = Assert.IsType<PostgresException>(
            dbEx.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            pg.SqlState);

        Assert.Equal(UniqueIndexName, pg.ConstraintName);
    }

    // Gerçek eşzamanlılık yarışını kurar ve sonuçlarını doğrular.
    private async Task RunRaceAsync(int participants)
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(pageCount: 250);
        var gate = new RaceGate(participants);

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, participants)
                .Select(_ => Task.Run(
                    () => RunAddAsync(userId, bookId, gate))));

        Assert.True(
            gate.AllArrived &&
            !outcomes.Any(o => o.Error is TimeoutException),
            $"Yarış kurulamadı (kapıya ulaşan: " +
            $"{gate.Arrived}/{participants}). Sonuçlar: {Describe(outcomes)}");

        var winners = outcomes
            .Where(o => o.Error is null)
            .ToList();

        var losers = outcomes
            .Where(o => o.Error is not null)
            .ToList();

        Assert.True(
            winners.Count == 1,
            $"Beklenen: 1 başarılı, {participants - 1} çakışma. " +
            $"Gözlenen: {Describe(outcomes)}");

        Assert.True(
            losers.All(o =>
                o.Error!.GetType() ==
                typeof(UserBookAlreadyExistsException)),
            $"Kaybedenlerin hepsi UserBookAlreadyExistsException " +
            $"olmalı. Gözlenen: {Describe(outcomes)}");

        foreach (var loser in losers)
        {
            AssertIsUserBookUniqueViolation(loser.Error!);
        }

        Assert.Equal(
            1,
            await CountUserBooksAsync(userId, bookId));
    }

    // 1. Normal kitap ekleme
    [PostgresFact]
    public async Task FirstAdd_OnPostgreSql_CreatesExactlyOneRow()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(pageCount: 120);

        var outcome = await RunAddAsync(userId, bookId, gate: null);

        Assert.True(outcome.Error is null, outcome.Error?.ToString());
        Assert.NotNull(outcome.Result);
        Assert.Equal(bookId, outcome.Result!.BookId);
        Assert.Equal(
            ReadingStatus.NotStarted,
            outcome.Result.Status);
        Assert.Equal(120, outcome.Result.PageCount);
        Assert.Equal(1, await CountUserBooksAsync(userId, bookId));
    }

    // 2. Sıralı tekrar ekleme: servis ön kontrolü
    [PostgresFact]
    public async Task SequentialDuplicateAdd_IsRejectedByServicePreCheck_NotByDatabase()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync();

        var first = await RunAddAsync(userId, bookId, gate: null);
        Assert.True(first.Error is null, first.Error?.ToString());

        var second = await RunAddAsync(userId, bookId, gate: null);

        var ex = Assert.IsType<InvalidOperationException>(second.Error);

        Assert.Equal(
            UserBookAlreadyExistsException.DefaultMessage,
            ex.Message);

        Assert.Equal(1, await CountUserBooksAsync(userId, bookId));
    }

    // 3. Repository benzersizlik hatasını dönüştürüyor mu?
    [PostgresFact]
    public async Task RepositoryAdd_UniqueViolationOnUserBookIndex_ThrowsUserBookAlreadyExists()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync();

        await using (var ctx1 = _db.CreateContext())
        {
            await new UserBookRepository(ctx1).AddAsync(
                new UserBook
                {
                    UserId = userId,
                    BookId = bookId,
                    PageCount = 321
                });
        }

        await using var ctx2 = _db.CreateContext();

        var duplicate = new UserBook
        {
            UserId = userId,
            BookId = bookId,
            PageCount = 999
        };

        var ex = await Assert.ThrowsAsync<UserBookAlreadyExistsException>(
            () => new UserBookRepository(ctx2).AddAsync(duplicate));

        AssertIsUserBookUniqueViolation(ex);

        Assert.Equal(
            EntityState.Detached,
            ctx2.Entry(duplicate).State);

        await using var verify = _db.CreateContext();

        var row = Assert.Single(
            await verify.UserBooks
                .Where(ub =>
                    ub.UserId == userId &&
                    ub.BookId == bookId)
                .ToListAsync());

        Assert.Equal(321, row.PageCount);
    }

    // 4. Foreign key hatası yanlışlıkla 409'a çevrilmemeli.
    [PostgresFact]
    public async Task RepositoryAdd_ForeignKeyViolation_IsNotTranslatedToConflict()
    {
        var userId = await SeedUserAsync();

        await using var ctx = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => new UserBookRepository(ctx).AddAsync(
                new UserBook
                {
                    UserId = userId,
                    BookId = int.MaxValue
                }));

        var pg = Assert.IsType<PostgresException>(ex.InnerException);

        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            pg.SqlState);
    }

    // 5. Primary key hatası yanlışlıkla 409'a çevrilmemeli.
    [PostgresFact]
    public async Task RepositoryAdd_PrimaryKeyViolation_IsNotTranslatedToConflict()
    {
        var userA = await SeedUserAsync();
        var bookA = await SeedBookAsync();
        var userB = await SeedUserAsync();
        var bookB = await SeedBookAsync();

        int existingId;

        await using (var ctx1 = _db.CreateContext())
        {
            var first = new UserBook
            {
                UserId = userA,
                BookId = bookA
            };

            await new UserBookRepository(ctx1).AddAsync(first);
            existingId = first.Id;
        }

        await using var ctx2 = _db.CreateContext();

        var clash = new UserBook
        {
            Id = existingId,
            UserId = userB,
            BookId = bookB
        };

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => new UserBookRepository(ctx2).AddAsync(clash));

        var pg = Assert.IsType<PostgresException>(ex.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            pg.SqlState);

        Assert.Equal("PK_UserBooks", pg.ConstraintName);
        Assert.NotEqual(UniqueIndexName, pg.ConstraintName);
    }

    // 6. Aynı kitap için iki eşzamanlı istek
    [PostgresFact]
    public Task ConcurrentAdd_TwoRequests_ExactlyOneWins()
        => RunRaceAsync(participants: 2);

    // 7. Aynı kitap için sekiz eşzamanlı istek
    [PostgresFact]
    public Task ConcurrentAdd_EightRequests_ExactlyOneWins()
        => RunRaceAsync(participants: 8);

    // 8. Farklı kullanıcılar aynı kitabı ekleyebilmeli.
    [PostgresFact]
    public async Task ConcurrentAdd_DifferentUsersSameBook_BothSucceed()
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var bookId = await SeedBookAsync();
        var gate = new RaceGate(2);

        var outcomes = await Task.WhenAll(
            Task.Run(() => RunAddAsync(userA, bookId, gate)),
            Task.Run(() => RunAddAsync(userB, bookId, gate)));

        Assert.True(
            gate.AllArrived,
            "Yarış kurulamadı.");

        Assert.All(
            outcomes,
            o => Assert.True(
                o.Error is null,
                o.Error?.ToString()));

        Assert.Equal(
            1,
            await CountUserBooksAsync(userA, bookId));

        Assert.Equal(
            1,
            await CountUserBooksAsync(userB, bookId));
    }
}
