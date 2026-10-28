using BookApp.Application.DTOs.Library;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using Xunit;

namespace BookApp.Tests;

public partial class ReadingGoalsAndDatesTests
{
    private record UserBookSnapshot(
        ReadingStatus Status,
        int? CurrentPage,
        DateTime? StartedAt,
        int? FinishedYear,
        int? FinishedMonth,
        int? FinishedDay,
        DateTime? DidNotFinishAt,
        bool IsFavorite,
        decimal? Rating,
        string? Review)
    {
        public static UserBookSnapshot From(UserBook ub) => new(
            ub.Status,
            ub.CurrentPage,
            ub.StartedAt,
            ub.FinishedYear,
            ub.FinishedMonth,
            ub.FinishedDay,
            ub.DidNotFinishAt,
            ub.IsFavorite,
            ub.Rating,
            ub.Review);
    }

    [Fact]
    public async Task UpdateStartedDate_OnReadingBook_UpdatesStartedAtSuccessfully()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Reading);
        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 10 }
        };

        var result = await service.UpdateStartedDateAsync(1, 1, request);

        Assert.NotNull(result.StartedAt);
        Assert.Equal(new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc), result.StartedAt);
        Assert.Equal(new DateOnly(2024, 3, 10), result.StartedDate);
        Assert.Equal(new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc), record.StartedAt);
    }

    [Fact]
    public async Task UpdateStartedDate_OnReadBook_WhenStartedBeforeFinished_Succeeds()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);
        record.FinishedYear = 2024;
        record.FinishedMonth = 5;
        record.FinishedDay = 20;

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 4, Day = 1 }
        };

        var result = await service.UpdateStartedDateAsync(1, 1, request);

        Assert.Equal(new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc), result.StartedAt);
        Assert.Equal(new DateOnly(2024, 4, 1), result.StartedDate);
    }

    [Fact]
    public async Task UpdateStartedDate_OnReadBook_SameStartAndFinishDate_Succeeds()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);
        record.FinishedYear = 2024;
        record.FinishedMonth = 5;
        record.FinishedDay = 20;

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 5, Day = 20 }
        };

        var result = await service.UpdateStartedDateAsync(1, 1, request);

        Assert.Equal(new DateTime(2024, 5, 20, 0, 0, 0, DateTimeKind.Utc), result.StartedAt);
    }

    [Fact]
    public async Task UpdateStartedDate_OnReadBook_WhenStartedAfterFinished_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);
        record.FinishedYear = 2024;
        record.FinishedMonth = 5;
        record.FinishedDay = 15;
        record.StartedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var beforeSnapshot = UserBookSnapshot.From(record);

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 5, Day = 16 }
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStartedDateAsync(1, 1, request));
        Assert.Contains("bitirme tarihinden", ex.Message);

        var afterSnapshot = UserBookSnapshot.From(record);
        Assert.Equal(beforeSnapshot, afterSnapshot);
    }

    [Fact]
    public async Task UpdateStartedDate_OnDidNotFinishBook_WhenStartedAfterDnf_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.DidNotFinish);
        record.DidNotFinishAt = new DateTime(2024, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var beforeSnapshot = UserBookSnapshot.From(record);

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 6, Day = 13 }
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStartedDateAsync(1, 1, request));
        Assert.Contains("bırakılma tarihinden", ex.Message);

        var afterSnapshot = UserBookSnapshot.From(record);
        Assert.Equal(beforeSnapshot, afterSnapshot);
    }

    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.NextUp)]
    public async Task UpdateStartedDate_OnNotStartedOrNextUp_ThrowsInvalidOperation(ReadingStatus status)
    {
        var (service, record) = CreateServiceWithBook(status);
        var beforeSnapshot = UserBookSnapshot.From(record);

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 1 }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStartedDateAsync(1, 1, request));
        Assert.Contains("Yalnızca 'Okunuyor', 'Okundu' veya 'Yarım bırakıldı'", ex.Message);

        var afterSnapshot = UserBookSnapshot.From(record);
        Assert.Equal(beforeSnapshot, afterSnapshot);
    }

    [Fact]
    public async Task UpdateStartedDate_NonExistingOrOtherUserBook_ThrowsKeyNotFound()
    {
        var (service, _) = CreateServiceWithBook(ReadingStatus.Reading, ownerUserId: 1);
        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 1 }
        };

        // Başka kullanıcının kaydı (userId: 2) -> 404
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateStartedDateAsync(2, 1, request));

        // Olmayan kitap (id: 999) -> 404
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateStartedDateAsync(1, 999, request));
    }

    [Fact]
    public async Task UpdateStartedDate_SameDateSentAgain_IsIdempotentAndReturns200()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Reading);
        record.StartedAt = new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc);

        var request = new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 2, Day = 15 }
        };

        var result = await service.UpdateStartedDateAsync(1, 1, request);
        Assert.Equal(new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc), result.StartedAt);
        Assert.Equal(new DateOnly(2024, 2, 15), result.StartedDate);
    }

    [Fact]
    public async Task PastReadBook_WithNullStartedAt_AndPartialOrFullFinishedDate_IsPreserved()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        // Geçmiş Read kitabı: Mart 2023'te bitmiş, StartedAt null
        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            StartedAt = null,
            FinishedYear = 2023,
            FinishedMonth = 3,
            FinishedDay = null,
            Book = new Book { Title = "Geçmiş Kitap", Author = "Yazar", PageCount = 250 }
        };
        repo.Books.Add(ub);

        // Kütüphane listesinde veya get işlemlerinde StartedAt null kalır, FinishedYear/Month korunur
        var list = await service.GetLibraryAsync(1, new LibraryQuery());
        var fetched = Assert.Single(list.Items);
        Assert.Null(fetched.StartedAt);
        Assert.Null(fetched.StartedDate);
        Assert.Equal(2023, fetched.FinishedYear);
        Assert.Equal(3, fetched.FinishedMonth);
        Assert.Null(fetched.FinishedDay);

        // Kullanıcı sonradan başlangıç tarihi ekleyebilmeli
        var updateResult = await service.UpdateStartedDateAsync(1, 1, new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2023, Month = 2, Day = 10 }
        });
        Assert.Equal(new DateTime(2023, 2, 10, 0, 0, 0, DateTimeKind.Utc), updateResult.StartedAt);
        Assert.Equal(new DateOnly(2023, 2, 10), updateResult.StartedDate);
        Assert.Equal(2023, updateResult.FinishedYear);
        Assert.Equal(3, updateResult.FinishedMonth);
    }

    [Fact]
    public async Task UpdateStatusAsync_DirectToRead_ValidationFirst_ThrowsAndPreservesEntity()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.DidNotFinish,
            StartedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            DidNotFinishAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Book = new Book { Title = "X", Author = "Y" }
        };
        repo.Books.Add(ub);
        var beforeSnapshot = UserBookSnapshot.From(ub);

        // Başlangıç tarihinden (1 Şubat 2024) önce bitirme tarihi girilirse hata vermeli
        var request = new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 15 }
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, request));
        Assert.Contains("başlangıç tarihinden", ex.Message);

        // Validation-first kuralı: ApplyStatusChange çağrılmamış olmalı, Status hala DidNotFinish olmalı
        var afterSnapshot = UserBookSnapshot.From(ub);
        Assert.Equal(beforeSnapshot, afterSnapshot);
        Assert.Equal(ReadingStatus.DidNotFinish, ub.Status);
        Assert.NotNull(ub.DidNotFinishAt);
    }

    [Fact]
    public async Task UpdateProgressAsync_DirectToRead_WhenStartedAtIsNull_DoesNotFabricateStartDate()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.NotStarted,
            StartedAt = null,
            CurrentPage = null,
            Book = new Book { Title = "Geçmiş Okuma", Author = "Yazar", PageCount = 300 }
        };
        repo.Books.Add(ub);

        // NotStarted kitabına doğrudan son sayfa giriliyor (300)
        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest
        {
            CurrentPage = 300,
            LocalDate = new CompletionDateDto { Year = 2023, Month = 5, Day = 15 }
        });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(300, result.CurrentPage);
        Assert.Equal(2023, result.FinishedYear);
        Assert.Equal(5, result.FinishedMonth);
        Assert.Equal(15, result.FinishedDay);
        // ÖNEMLİ KURAL: StartedAt bugünün tarihi yapılmaz, null kalır
        Assert.Null(result.StartedAt);
        Assert.Null(result.StartedDate);
        Assert.Null(ub.StartedAt);
    }

    [Fact]
    public async Task UpdateProgressAsync_IncrementalProgress_PreservesActiveReadingStartedAtBehavior()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.NotStarted,
            StartedAt = null,
            CurrentPage = null,
            Book = new Book { Title = "Aktif Okunan", Author = "Yazar", PageCount = 300 }
        };
        repo.Books.Add(ub);

        // Kısmi ilerleme giriliyor (50 sayfa) -> Reading durumuna geçmeli ve StartedAt atanmalı
        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 50 });

        Assert.Equal(ReadingStatus.Reading, result.Status);
        Assert.Equal(50, result.CurrentPage);
        Assert.NotNull(result.StartedAt);
        Assert.NotNull(ub.StartedAt);
    }

    [Fact]
    public async Task UpdateProgressAsync_WhenStartedAtExists_ReachingEnd_PreservesExistingStartedAt()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var existingStart = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            StartedAt = existingStart,
            CurrentPage = 100,
            Book = new Book { Title = "Biten Kitap", Author = "Yazar", PageCount = 200 }
        };
        repo.Books.Add(ub);

        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest
        {
            CurrentPage = 200,
            LocalDate = new CompletionDateDto { Year = 2024, Month = 2, Day = 20 }
        });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(existingStart, result.StartedAt);
        Assert.Equal(new DateOnly(2024, 1, 15), result.StartedDate);
        Assert.Equal(2024, result.FinishedYear);
        Assert.Equal(2, result.FinishedMonth);
        Assert.Equal(20, result.FinishedDay);
    }

    [Fact]
    public async Task UpdateProgressAsync_InvalidLocalDateBeforeStartedAt_ThrowsAndPreservesEntity()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            StartedAt = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            CurrentPage = 50,
            Book = new Book { Title = "Test", Author = "Yazar", PageCount = 100 }
        };
        repo.Books.Add(ub);
        var beforeSnapshot = UserBookSnapshot.From(ub);

        // Bitirme tarihi (1 Mayıs 2024) başlangıç tarihinden (10 Mayıs 2024) önce
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateProgressAsync(1, 1, new UpdateProgressRequest
            {
                CurrentPage = 100,
                LocalDate = new CompletionDateDto { Year = 2024, Month = 5, Day = 1 }
            }));
        Assert.Contains("başlangıç tarihinden", ex.Message);

        var afterSnapshot = UserBookSnapshot.From(ub);
        Assert.Equal(beforeSnapshot, afterSnapshot);
        Assert.Equal(50, ub.CurrentPage);
        Assert.Equal(ReadingStatus.Reading, ub.Status);
    }

    [Fact]
    public async Task UpdateProgressAsync_ScenarioC_UtcToleranceBoundary_AlignsToStartedAtDate()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        // Sunucu UTC takviminden tam 1 gün ileride StartedAt (istemci saat dilimi toleransı)
        var tomorrowUtc = DateTime.UtcNow.Date.AddDays(1);
        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            StartedAt = tomorrowUtc,
            CurrentPage = 90,
            Book = new Book { Title = "Tolerans Kitabı", Author = "Yazar", PageCount = 100 }
        };
        repo.Books.Add(ub);

        // LocalDate gönderilmeden son sayfaya gelindiğinde sistem bitirme gününü StartedAt gününe hizalar
        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 100 });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(tomorrowUtc.Year, result.FinishedYear);
        Assert.Equal(tomorrowUtc.Month, result.FinishedMonth);
        Assert.Equal(tomorrowUtc.Day, result.FinishedDay);
        Assert.Equal(tomorrowUtc, result.StartedAt);
    }

    [Fact]
    public async Task UpdateFinishedDateAsync_StartedAtConflict_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);
        record.StartedAt = new DateTime(2024, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        record.FinishedYear = 2024;
        record.FinishedMonth = 5;
        record.FinishedDay = 1;
        var beforeSnapshot = UserBookSnapshot.From(record);

        // Bitirme tarihini başlangıçtan önceye çekmeye çalışmak
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateFinishedDateAsync(1, 1, new UpdateFinishedDateRequest
            {
                FinishedDate = new CompletionDateDto { Year = 2024, Month = 4, Day = 10 }
            }));
        Assert.Contains("başlangıç tarihinden", ex.Message);

        var afterSnapshot = UserBookSnapshot.From(record);
        Assert.Equal(beforeSnapshot, afterSnapshot);
    }
}
