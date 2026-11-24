using BookApp.Application.DTOs.External;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookApp.Tests.PostgreSql;

// 7.4-C.5: ISBN benzersizligi ve eszamanlilik testleri.
// Yalnizca korumali PostgreSQL test veritabani kullanilir.
[Collection(PostgresCollection.Name)]
public class PostgresBookIsbnUniqueTests
{
    private const string UniqueIndexName = "IX_Books_Isbn";

    private readonly PostgresDatabaseFixture _db;

    public PostgresBookIsbnUniqueTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    // Her test icin rastgele, gecerli ISBN-13 uretir.
    private static string NewIsbn()
    {
        var first12 =
            $"978{Random.Shared.NextInt64(0, 1_000_000_000):D9}";

        var sum = 0;

        for (var i = 0; i < first12.Length; i++)
        {
            var digit = first12[i] - '0';
            sum += digit * (i % 2 == 0 ? 1 : 3);
        }

        var checkDigit = (10 - sum % 10) % 10;

        return first12 + checkDigit;
    }

    private static Book NewBook(string? isbn)
    {
        return new Book
        {
            Title = $"PG C5 Book {Guid.NewGuid():N}",
            Author = "PostgreSQL C5 Test Author",
            Isbn = isbn,
            PageCount = 200
        };
    }

    private async Task<Book> AddBookAsync(string? isbn)
    {
        await using var context = _db.CreateContext();

        var book = NewBook(isbn);

        await new BookRepository(context).AddAsync(book);

        return book;
    }

    private async Task<int> CountByIsbnAsync(string isbn)
    {
        await using var context = _db.CreateContext();

        return await context.Books.CountAsync(b => b.Isbn == isbn);
    }

    private static void AssertIsbnUniqueViolation(
        BookIsbnAlreadyExistsException exception)
    {
        Assert.Equal(
            BookIsbnAlreadyExistsException.DefaultMessage,
            exception.Message);

        var dbException =
            Assert.IsType<DbUpdateException>(
                exception.InnerException);

        var postgresException =
            Assert.IsType<PostgresException>(
                dbException.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgresException.SqlState);

        Assert.Equal(
            UniqueIndexName,
            postgresException.ConstraintName);
    }

    // 1. Ayni ISBN ikinci kez eklenemez.
    [PostgresFact]
    public async Task DuplicateIsbn_ThrowsSpecificException()
    {
        var isbn = NewIsbn();

        await AddBookAsync(isbn);

        await using var context = _db.CreateContext();

        var duplicate = NewBook(isbn);

        var exception =
            await Assert.ThrowsAsync<BookIsbnAlreadyExistsException>(
                () => new BookRepository(context).AddAsync(duplicate));

        AssertIsbnUniqueViolation(exception);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(duplicate).State);

        Assert.Equal(1, await CountByIsbnAsync(isbn));
    }

    // 2. ISBN'si bilinmeyen birden fazla kitap eklenebilir.
    [PostgresFact]
    public async Task NullIsbn_MultipleBooksAreAllowed()
    {
        var first = await AddBookAsync(null);
        var second = await AddBookAsync(null);

        Assert.NotEqual(first.Id, second.Id);

        await using var context = _db.CreateContext();

        Assert.Null(
            (await context.Books.SingleAsync(
                b => b.Id == first.Id)).Isbn);

        Assert.Null(
            (await context.Books.SingleAsync(
                b => b.Id == second.Id)).Isbn);
    }

    // 3. Farkli ISBN'li kitaplar ayri kaydedilebilir.
    [PostgresFact]
    public async Task DifferentIsbns_BothBooksAreAllowed()
    {
        var firstIsbn = NewIsbn();
        var secondIsbn = NewIsbn();

        while (secondIsbn == firstIsbn)
        {
            secondIsbn = NewIsbn();
        }

        var first = await AddBookAsync(firstIsbn);
        var second = await AddBookAsync(secondIsbn);

        Assert.NotEqual(first.Id, second.Id);

        Assert.Equal(1, await CountByIsbnAsync(firstIsbn));
        Assert.Equal(1, await CountByIsbnAsync(secondIsbn));
    }

    // 4. Admin guncellemesindeki ISBN cakismasi engellenir.
    [PostgresFact]
    public async Task UpdateToExistingIsbn_ThrowsSpecificException()
    {
        var firstIsbn = NewIsbn();
        var secondIsbn = NewIsbn();

        while (secondIsbn == firstIsbn)
        {
            secondIsbn = NewIsbn();
        }

        var first = await AddBookAsync(firstIsbn);
        var second = await AddBookAsync(secondIsbn);

        await using var context = _db.CreateContext();

        var bookToUpdate =
            await context.Books.SingleAsync(
                b => b.Id == second.Id);

        bookToUpdate.Isbn = firstIsbn;

        var exception =
            await Assert.ThrowsAsync<BookIsbnAlreadyExistsException>(
                () => new BookRepository(context)
                    .UpdateAsync(bookToUpdate));

        AssertIsbnUniqueViolation(exception);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(bookToUpdate).State);

        await using var verify = _db.CreateContext();

        var unchanged =
            await verify.Books.AsNoTracking()
                .SingleAsync(b => b.Id == second.Id);

        Assert.Equal(secondIsbn, unchanged.Isbn);
        Assert.Equal(1, await CountByIsbnAsync(first.Isbn!));
    }

    // 5. Books primary key hatasi ISBN hatasina cevrilmez.
    [PostgresFact]
    public async Task PrimaryKeyViolation_IsNotTranslatedToIsbnConflict()
    {
        var existing = await AddBookAsync(NewIsbn());

        await using var context = _db.CreateContext();

        var conflictingBook = NewBook(NewIsbn());

        conflictingBook.Id = existing.Id;

        var exception =
            await Assert.ThrowsAsync<DbUpdateException>(
                () => new BookRepository(context)
                    .AddAsync(conflictingBook));

        var postgresException =
            Assert.IsType<PostgresException>(
                exception.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgresException.SqlState);

        Assert.Equal(
            "PK_Books",
            postgresException.ConstraintName);

        Assert.NotEqual(
            UniqueIndexName,
            postgresException.ConstraintName);
    }

    // Dis kitap servisi yerine kullanilan kontrollu sahte servis.
    private sealed class GateBookLookupService : IBookLookupService
    {
        private readonly RaceGate _gate;

        public GateBookLookupService(RaceGate gate)
        {
            _gate = gate;
        }

        public async Task<ExternalBookDto?> SearchByIsbnAsync(
            string isbn)
        {
            // Iki istek de ilk veritabani kontrolunu
            // gecmeden ekleme asamasina ilerleyemez.
            await _gate.ArriveAndWaitAsync(
                TimeSpan.FromSeconds(20));

            return new ExternalBookDto
            {
                Title = "PG C5 Concurrent ISBN Test",
                Author = "PostgreSQL Test Author",
                Isbn = isbn,
                PageCount = 250
            };
        }
    }

    private async Task<(int? BookId, Exception? Error)> RunFetchAsync(
        string isbn,
        RaceGate gate)
    {
        // Her eszamanli istek ayri DbContext kullanir.
        await using var context = _db.CreateContext();

        var service = new BookService(
            new BookRepository(context),
            new GateBookLookupService(gate));

        try
        {
            var result =
                await service.FindOrFetchByIsbnAsync(isbn);

            return (result.Id, null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    // 6. Iki eszamanli ISBN aramasi tek kitap olusturur.
    // Iki istek de ayni kitap kaydini dondurmelidir.
    [PostgresFact]
    public async Task ConcurrentFindOrFetch_ReturnsSameBook()
    {
        var isbn = NewIsbn();
        var gate = new RaceGate(2);

        var outcomes = await Task.WhenAll(
            Task.Run(() => RunFetchAsync(isbn, gate)),
            Task.Run(() => RunFetchAsync(isbn, gate)));

        Assert.True(
            gate.AllArrived,
            "Iki istek de eszamanlilik noktasina ulasmali.");

        foreach (var outcome in outcomes)
        {
            Assert.True(
                outcome.Error is null,
                outcome.Error?.ToString());

            Assert.NotNull(outcome.BookId);
        }

        Assert.Equal(
            outcomes[0].BookId,
            outcomes[1].BookId);

        Assert.Equal(
            1,
            await CountByIsbnAsync(isbn));

        await using var verify = _db.CreateContext();

        var savedBook =
            await verify.Books.AsNoTracking()
                .SingleAsync(b => b.Isbn == isbn);

        Assert.Equal(
            savedBook.Id,
            outcomes[0].BookId);
    }
}
