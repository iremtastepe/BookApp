using BookApp.Application.DTOs.Library;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using Xunit;

namespace BookApp.Tests;

// 7.3: Durum geçiş kuralları (K1, K3, K4, K5, K6, K7).
// ReadingGoalsAndDatesTests partial sınıfının parçasıdır: FakeUserBookRepository,
// CreateServiceWithBook ve UserBookSnapshot yardımcılarını diğer dosyalardan kullanır.
public partial class ReadingGoalsAndDatesTests
{
    private static DateTime TrUtc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static UpdateUserBookStatusRequest TrStatus(ReadingStatus status)
        => new() { Status = status };

    private static UpdateUserBookStatusRequest TrRead(int year, int month, int? day = null)
        => new()
        {
            Status = ReadingStatus.Read,
            FinishedDate = new CompletionDateDto { Year = year, Month = month, Day = day }
        };

    // Verilen duruma uygun, gerçekçi alanları dolu bir kayıt hazırlar (puan/yorum CreateServiceWithBook'tan gelir).
    private static (UserBookService Service, UserBook Record) CreateTransitionBook(ReadingStatus status, bool withStartedAt = true)
    {
        var (service, record) = CreateServiceWithBook(status);
        record.CurrentPage = 40;
        record.Book.PageCount = 100;
        record.IsFavorite = status == ReadingStatus.Read;

        if (status != ReadingStatus.NotStarted && status != ReadingStatus.NextUp && withStartedAt)
            record.StartedAt = TrUtc(2024, 1, 10);

        if (status == ReadingStatus.Read)
        {
            record.FinishedYear = 2024;
            record.FinishedMonth = 3;
            record.FinishedDay = 5;
        }

        if (status == ReadingStatus.DidNotFinish)
            record.DidNotFinishAt = TrUtc(2024, 2, 1);

        return (service, record);
    }

    // ---------- K1: Reading geçişi ve StartedAt ----------

    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.NextUp)]
    public async Task Transition_NotStartedOrNextUp_ToReading_AssignsStartedAt(ReadingStatus from)
    {
        var (service, record) = CreateTransitionBook(from);
        Assert.Null(record.StartedAt);

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(ReadingStatus.Reading, result.Status);
        Assert.NotNull(record.StartedAt);
        Assert.True(Math.Abs((DateTime.UtcNow - record.StartedAt!.Value).TotalMinutes) < 5);
    }

    [Theory]
    [InlineData(ReadingStatus.Read)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task Transition_ReadOrDidNotFinish_ToReading_WithoutStartedAt_DoesNotFabricateStartedAt(ReadingStatus from)
    {
        var (service, record) = CreateTransitionBook(from, withStartedAt: false);
        Assert.Null(record.StartedAt);

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(ReadingStatus.Reading, record.Status);
        Assert.Null(record.StartedAt);
        // Reading'e geçişte bitiş/bırakma bilgileri ve favori temizlenir
        Assert.Null(record.FinishedYear);
        Assert.Null(record.FinishedMonth);
        Assert.Null(record.FinishedDay);
        Assert.Null(record.DidNotFinishAt);
        Assert.False(record.IsFavorite);
    }

    [Theory]
    [InlineData(ReadingStatus.Read)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task Transition_ReadOrDidNotFinish_ToReading_WithStartedAt_PreservesStartedAt(ReadingStatus from)
    {
        var (service, record) = CreateTransitionBook(from);
        var originalStart = record.StartedAt;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(originalStart, record.StartedAt);
    }

    [Fact]
    public async Task Transition_ReadingToReading_DoesNotChangeStartedAt()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Reading);
        var originalStart = record.StartedAt;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(ReadingStatus.Reading, record.Status);
        Assert.Equal(originalStart, record.StartedAt);
        Assert.Equal(40, record.CurrentPage);
    }

    [Fact]
    public async Task Transition_PastReadBookWithoutStartedAt_ReadingThenReadAgain_Works()
    {
        // Eski tuzak: StartedAt boş geçmiş bir Read kitabı Reading'e alınınca bugünün tarihi uydurulursa,
        // 2024'teki yeni bitiş tarihi "başlangıçtan önce" sayılıp reddedilirdi.
        var (service, record) = CreateTransitionBook(ReadingStatus.Read, withStartedAt: false);

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));
        Assert.Null(record.StartedAt);

        var result = await service.UpdateStatusAsync(1, 1, TrRead(2024, 6, 1));

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Null(record.StartedAt);
        Assert.Equal(2024, record.FinishedYear);
        Assert.Equal(6, record.FinishedMonth);
        Assert.Equal(1, record.FinishedDay);
    }

    // ---------- K7: NotStarted / NextUp'a dönüş ----------

    [Theory]
    [InlineData(ReadingStatus.Reading, ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.Reading, ReadingStatus.NextUp)]
    [InlineData(ReadingStatus.Read, ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.Read, ReadingStatus.NextUp)]
    [InlineData(ReadingStatus.DidNotFinish, ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.DidNotFinish, ReadingStatus.NextUp)]
    public async Task Transition_ToNotStartedOrNextUp_ClearsDatesAndFavorite_KeepsRatingReviewAndPage(ReadingStatus from, ReadingStatus to)
    {
        var (service, record) = CreateTransitionBook(from);
        record.IsFavorite = true;

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(to));

        Assert.Equal(to, result.Status);
        Assert.Null(record.StartedAt);
        Assert.Null(record.FinishedYear);
        Assert.Null(record.FinishedMonth);
        Assert.Null(record.FinishedDay);
        Assert.Null(record.DidNotFinishAt);
        Assert.False(record.IsFavorite);
        // K4: puan ve yorum korunur, sayfa ilerlemesine dokunulmaz
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
        Assert.Equal(40, record.CurrentPage);
    }

    // ---------- DidNotFinish geçişleri ----------

    [Fact]
    public async Task Transition_ReadingToDidNotFinish_SetsDidNotFinishAt_KeepsStartedAt()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Reading);
        var originalStart = record.StartedAt;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.DidNotFinish));

        Assert.Equal(ReadingStatus.DidNotFinish, record.Status);
        Assert.NotNull(record.DidNotFinishAt);
        Assert.Equal(originalStart, record.StartedAt);
    }

    [Fact]
    public async Task Transition_DidNotFinishToDidNotFinish_KeepsExistingDidNotFinishAt()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.DidNotFinish);
        var originalDnf = record.DidNotFinishAt;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.DidNotFinish));

        Assert.Equal(originalDnf, record.DidNotFinishAt);
    }

    [Fact]
    public async Task Transition_ReadToDidNotFinish_ClearsFinishedDateAndFavorite_KeepsStartedAtRatingReview()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var originalStart = record.StartedAt;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.DidNotFinish));

        Assert.Equal(ReadingStatus.DidNotFinish, record.Status);
        Assert.Null(record.FinishedYear);
        Assert.Null(record.FinishedMonth);
        Assert.Null(record.FinishedDay);
        Assert.False(record.IsFavorite);
        Assert.NotNull(record.DidNotFinishAt);
        Assert.Equal(originalStart, record.StartedAt);
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task Transition_DidNotFinishToRead_ClearsDidNotFinishAt_SetsFinishedDate_KeepsStartedAt()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.DidNotFinish);
        var originalStart = record.StartedAt;

        await service.UpdateStatusAsync(1, 1, TrRead(2024, 4, 20));

        Assert.Equal(ReadingStatus.Read, record.Status);
        Assert.Null(record.DidNotFinishAt);
        Assert.Equal(2024, record.FinishedYear);
        Assert.Equal(4, record.FinishedMonth);
        Assert.Equal(20, record.FinishedDay);
        Assert.Equal(originalStart, record.StartedAt);
    }

    // ---------- K3 / K4: favori, puan, yorum ----------

    [Fact]
    public async Task Transition_LeavingRead_ClearsFavorite_AndReturningToReadDoesNotRestoreIt()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        Assert.True(record.IsFavorite);

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));
        Assert.False(record.IsFavorite);

        await service.UpdateStatusAsync(1, 1, TrRead(2024, 7, 1));

        Assert.Equal(ReadingStatus.Read, record.Status);
        Assert.False(record.IsFavorite);
        // Puan ve yorum tüm zincir boyunca korunur
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    // ---------- K5: Read -> Read ----------

    [Fact]
    public async Task ReadToRead_WithoutDate_ReturnsUnchanged()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Read));

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_SameFullDate_ReturnsUnchanged()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        await service.UpdateStatusAsync(1, 1, TrRead(2024, 3, 5));

        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_SamePartialDate_ReturnsUnchanged()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        record.FinishedDay = null; // yalnızca yıl + ay biliniyor
        var before = UserBookSnapshot.From(record);

        await service.UpdateStatusAsync(1, 1, TrRead(2024, 3));

        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_DifferentDate_ThrowsWithHint_AndPreservesEntity()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, TrRead(2024, 3, 6)));

        Assert.Contains("finished-date", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_DateGivenButStoredDateIsUnknown_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        record.FinishedYear = null;
        record.FinishedMonth = null;
        record.FinishedDay = null;
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, TrRead(2024, 3, 5)));

        Assert.Contains("finished-date", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_UnknownWhenAlreadyUnknown_ReturnsUnchanged()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        record.FinishedYear = null;
        record.FinishedMonth = null;
        record.FinishedDay = null;
        var before = UserBookSnapshot.From(record);

        await service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_UnknownWhenDateExists_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest
            {
                Status = ReadingStatus.Read,
                FinishedDateUnknown = true
            }));

        Assert.Contains("finished-date", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_DateAndUnknownTogether_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest
            {
                Status = ReadingStatus.Read,
                FinishedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 5 },
                FinishedDateUnknown = true
            }));

        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task ReadToRead_InvalidDate_ThrowsAndPreservesEntity()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);
        var before = UserBookSnapshot.From(record);

        // 31 Şubat gerçek bir tarih değildir
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, TrRead(2024, 2, 31)));

        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    // ---------- K6: DidNotFinish iken son sayfa ----------

    [Fact]
    public async Task Progress_DidNotFinish_ReachingLastPage_StaysDidNotFinish()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.DidNotFinish);
        var originalDnf = record.DidNotFinishAt;

        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 100 });

        Assert.Equal(ReadingStatus.DidNotFinish, result.Status);
        Assert.Equal(100, record.CurrentPage);
        Assert.Equal(originalDnf, record.DidNotFinishAt);
        Assert.Null(record.FinishedYear);
        Assert.Null(record.FinishedMonth);
        Assert.Null(record.FinishedDay);
    }

    [Fact]
    public async Task Progress_Read_ReachingLastPage_StaysReadAndKeepsFinishedDate()
    {
        var (service, record) = CreateTransitionBook(ReadingStatus.Read);

        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 100 });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(100, record.CurrentPage);
        Assert.Equal(2024, record.FinishedYear);
        Assert.Equal(3, record.FinishedMonth);
        Assert.Equal(5, record.FinishedDay);
    }
}