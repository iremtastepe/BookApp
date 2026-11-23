using BookApp.Application.DTOs.Library;
using BookApp.Application.Exceptions;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Tests.PostgreSql;

// 7.4-C.3 (R4): Gerçek PostgreSQL üzerinde UserBook eşzamanlı güncelleme ve xmin concurrency testleri.
// Yalnızca BookAppDb_Test kullanılır. Testler mevcut verileri silmez.
[Collection(PostgresCollection.Name)]
public class PostgresConcurrentUpdateTests
{
    private readonly PostgresDatabaseFixture _db;

    public PostgresConcurrentUpdateTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private async Task<int> SeedUserAsync()
    {
        var tag = Guid.NewGuid().ToString("N");
        await using var ctx = _db.CreateContext();

        var user = new User
        {
            UserName = $"pgr4_{tag}",
            NormalizedUserName = $"PGR4_{tag}".ToUpperInvariant(),
            Email = $"pgr4_{tag}@test.local",
            NormalizedEmail = $"PGR4_{tag}@TEST.LOCAL".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedBookAsync(int? pageCount = 300)
    {
        var tag = Guid.NewGuid().ToString("N");
        await using var ctx = _db.CreateContext();

        var book = new Book
        {
            Title = $"PG R4 Book {tag}",
            Author = "PG R4 Author",
            PageCount = pageCount
        };

        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        return book.Id;
    }

    private async Task<int> SeedUserBookAsync(int userId, int bookId, ReadingStatus status = ReadingStatus.Reading, int? currentPage = 50)
    {
        await using var ctx = _db.CreateContext();

        var userBook = new UserBook
        {
            UserId = userId,
            BookId = bookId,
            Status = status,
            CurrentPage = currentPage,
            PageCount = 300,
            AddedAt = DateTime.UtcNow
        };

        ctx.UserBooks.Add(userBook);
        await ctx.SaveChangesAsync();
        return userBook.Id;
    }

    // 1. İki farklı DbContext aynı UserBook kaydını okur. İlk güncelleme başarılı olur, ikinci güncelleme UserBookConcurrencyException alır.
    [PostgresFact]
    public async Task ConcurrentUpdate_TwoDbContexts_SecondFailsWithConcurrencyException_AndFirstWins()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(300);
        var userBookId = await SeedUserBookAsync(userId, bookId, ReadingStatus.Reading, 50);

        // 1. İki farklı context aynı sürümdeki kaydı okur
        await using var ctx1 = _db.CreateContext();
        await using var ctx2 = _db.CreateContext();

        var repo1 = new UserBookRepository(ctx1);
        var repo2 = new UserBookRepository(ctx2);

        var ub1 = await repo1.GetByIdWithBookAsync(userBookId, userId);
        var ub2 = await repo2.GetByIdWithBookAsync(userBookId, userId);

        Assert.NotNull(ub1);
        Assert.NotNull(ub2);
        Assert.Equal(ub1!.Version, ub2!.Version);

        // 2. İlk işlem kaydı günceller ve kaydeder
        ub1.CurrentPage = 100;
        await repo1.UpdateAsync(ub1);

        // 3. İkinci işlem eski sürüm üzerinden güncelleme yapmayı dener -> Concurrency hatası fırlatılmalı
        ub2.CurrentPage = 150;
        var ex = await Assert.ThrowsAsync<UserBookConcurrencyException>(
            () => repo2.UpdateAsync(ub2));

        Assert.NotNull(ex.InnerException);
        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);

        // 4. Veritabanındaki verinin ilk işlemin kaydettiği (100) olarak korunduğu doğrulanır
        await using var verifyCtx = _db.CreateContext();
        var finalUb = await verifyCtx.UserBooks.FindAsync(userBookId);
        Assert.NotNull(finalUb);
        Assert.Equal(100, finalUb!.CurrentPage);
        Assert.NotEqual(150, finalUb.CurrentPage);
    }

    // 2. Arşivleme ve Durum Değiştirme çakışması
    [PostgresFact]
    public async Task ConcurrentUpdate_ArchiveAndStatusChange_OneWinsOtherGetsConflict()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(300);
        var userBookId = await SeedUserBookAsync(userId, bookId, ReadingStatus.Reading, 50);

        await using var ctx1 = _db.CreateContext();
        await using var ctx2 = _db.CreateContext();

        var repo1 = new UserBookRepository(ctx1);
        var repo2 = new UserBookRepository(ctx2);

        var ub1 = await repo1.GetByIdWithBookAsync(userBookId, userId);
        var ub2 = await repo2.GetByIdWithBookAsync(userBookId, userId);

        Assert.NotNull(ub1);
        Assert.NotNull(ub2);

        // Birinci işlem arşive kaldırır
        ub1!.ArchivedAt = DateTime.UtcNow;
        await repo1.UpdateAsync(ub1);

        // İkinci işlem durumu değiştirip kaydetmeye çalışır -> Eski sürüm olduğu için concurrency hatası almalı
        ub2!.Status = ReadingStatus.DidNotFinish;
        ub2.DidNotFinishAt = DateTime.UtcNow;

        var ex = await Assert.ThrowsAsync<UserBookConcurrencyException>(
            () => repo2.UpdateAsync(ub2));

        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);

        // Doğrulama: Kitap arşivde kaldı, durumu bozulmadı
        await using var verifyCtx = _db.CreateContext();
        var finalUb = await verifyCtx.UserBooks.FindAsync(userBookId);
        Assert.NotNull(finalUb!.ArchivedAt);
        Assert.Equal(ReadingStatus.Reading, finalUb.Status);
    }

    // 3. Read (Tamamlandı) ve CurrentPage ilerlemesi çakışması
    [PostgresFact]
    public async Task ConcurrentUpdate_ReadAndProgress_FirstWinsAndIntegrityMaintained()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(200);
        var userBookId = await SeedUserBookAsync(userId, bookId, ReadingStatus.Reading, 150);

        await using var ctx1 = _db.CreateContext();
        await using var ctx2 = _db.CreateContext();

        var repo1 = new UserBookRepository(ctx1);
        var repo2 = new UserBookRepository(ctx2);

        var ub1 = await repo1.GetByIdWithBookAsync(userBookId, userId);
        var ub2 = await repo2.GetByIdWithBookAsync(userBookId, userId);

        Assert.NotNull(ub1);
        Assert.NotNull(ub2);

        // İşlem 1: Kitap Read yapılır, bitirme tarihi girilir
        ub1!.Status = ReadingStatus.Read;
        ub1.CurrentPage = 200;
        ub1.FinishedYear = 2026;
        ub1.FinishedMonth = 5;
        ub1.FinishedDay = 15;
        await repo1.UpdateAsync(ub1);

        // İşlem 2: Aynı anda ara sayfa ilerlemesi girilmeye çalışılır
        ub2!.CurrentPage = 180;
        var ex = await Assert.ThrowsAsync<UserBookConcurrencyException>(
            () => repo2.UpdateAsync(ub2));

        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);

        // Doğrulama: İşlem 1'in Read durumu ve tamamlanma tarihi korundu
        await using var verifyCtx = _db.CreateContext();
        var finalUb = await verifyCtx.UserBooks.FindAsync(userBookId);
        Assert.Equal(ReadingStatus.Read, finalUb!.Status);
        Assert.Equal(200, finalUb.CurrentPage);
        Assert.Equal(2026, finalUb.FinishedYear);
    }

    // 4. Sıralı iki başarılı güncelleme (her biri taze okunduğunda xmin yenilenir ve sorunsuz çalışır)
    [PostgresFact]
    public async Task SequentialUpdates_WithFreshReads_BothSucceed()
    {
        var userId = await SeedUserAsync();
        var bookId = await SeedBookAsync(200);
        var userBookId = await SeedUserBookAsync(userId, bookId, ReadingStatus.Reading, 20);

        // İlk güncelleme
        await using (var ctx1 = _db.CreateContext())
        {
            var repo1 = new UserBookRepository(ctx1);
            var ub1 = await repo1.GetByIdWithBookAsync(userBookId, userId);
            ub1!.CurrentPage = 30;
            await repo1.UpdateAsync(ub1);
        }

        // İkinci güncelleme (taze context ve taze sürüm okunarak)
        await using (var ctx2 = _db.CreateContext())
        {
            var repo2 = new UserBookRepository(ctx2);
            var ub2 = await repo2.GetByIdWithBookAsync(userBookId, userId);
            Assert.Equal(30, ub2!.CurrentPage);
            ub2.CurrentPage = 40;
            await repo2.UpdateAsync(ub2);
        }

        // Sonuç 40 olmalı
        await using (var verifyCtx = _db.CreateContext())
        {
            var finalUb = await verifyCtx.UserBooks.FindAsync(userBookId);
            Assert.Equal(40, finalUb!.CurrentPage);
        }
    }
}
