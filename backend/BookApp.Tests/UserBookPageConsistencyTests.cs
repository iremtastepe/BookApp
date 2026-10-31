using BookApp.Application.DTOs.Library;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using Xunit;

namespace BookApp.Tests;

// 7.4: Sayfa sayısı / CurrentPage / durum tutarlılığı (K1, K2, ek geçişler ve Read'de ilerleme koruması).
// ReadingGoalsAndDatesTests partial sınıfının parçasıdır: FakeUserBookRepository, CreateServiceWithBook,
// UserBookSnapshot, TrUtc, TrStatus ve TrRead yardımcılarını diğer dosyalardan kullanır.
public partial class ReadingGoalsAndDatesTests
{
    // Verilen durum, sayfa ve toplam sayfa sayısıyla gerçekçi bir kayıt hazırlar.
    // Başlangıç tarihi NotStarted/NextUp dışındaki durumlarda dolu gelir (7.3 kuralı), Read'de bitiş tarihi ve favori de dolu.
    private static (UserBookService Service, UserBook Record) CreatePageBook(
        ReadingStatus status, int? currentPage, int? pageCount)
    {
        var (service, record) = CreateServiceWithBook(status);
        record.CurrentPage = currentPage;
        record.Book.PageCount = pageCount;

        if (status is not (ReadingStatus.NotStarted or ReadingStatus.NextUp))
            record.StartedAt = TrUtc(2024, 1, 10);

        if (status == ReadingStatus.Read)
        {
            record.FinishedYear = 2024;
            record.FinishedMonth = 3;
            record.FinishedDay = 5;
            record.IsFavorite = true;
        }

        if (status == ReadingStatus.DidNotFinish)
            record.DidNotFinishAt = TrUtc(2024, 2, 1);

        return (service, record);
    }

    // ---------- K1: Read'e geçişte sayfa ----------

    [Theory]
    [InlineData(ReadingStatus.Reading, 120)]
    [InlineData(ReadingStatus.DidNotFinish, 150)]
    [InlineData(ReadingStatus.NotStarted, null)]
    [InlineData(ReadingStatus.NextUp, null)]
    public async Task PageRules_ManualRead_WithKnownPageCount_SetsCurrentPageToPageCount(ReadingStatus from, int? currentPage)
    {
        var (service, record) = CreatePageBook(from, currentPage, pageCount: 300);

        var result = await service.UpdateStatusAsync(1, 1, TrRead(2024, 4, 20));

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(300, result.CurrentPage);
        Assert.Equal(300, record.CurrentPage);
        Assert.Equal(100d, result.ProgressPercentage);
    }

    [Theory]
    [InlineData(null, 120)]
    [InlineData(0, 120)]
    [InlineData(null, null)]
    public async Task PageRules_ManualRead_WithUnknownPageCount_KeepsCurrentPage(int? pageCount, int? currentPage)
    {
        var (service, record) = CreatePageBook(ReadingStatus.Reading, currentPage, pageCount);

        var result = await service.UpdateStatusAsync(1, 1, TrRead(2024, 4, 20));

        // Sayfa sayısı bilinmiyor diye reddedilmez, sayı da uydurulmaz
        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(currentPage, record.CurrentPage);
    }

    [Fact]
    public async Task PageRules_ManualRead_WhenValidationFails_DoesNotTouchCurrentPage()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Reading, currentPage: 120, pageCount: 300);
        record.StartedAt = TrUtc(2024, 5, 10);
        var before = UserBookSnapshot.From(record);

        // Bitirme tarihi (1 Mayıs) başlangıç tarihinden (10 Mayıs) önce -> 400
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, TrRead(2024, 5, 1)));

        Assert.Equal(before, UserBookSnapshot.From(record));
        Assert.Equal(120, record.CurrentPage);
        Assert.Equal(ReadingStatus.Reading, record.Status);
    }

    [Fact]
    public async Task PageRules_ReadToRead_LegacyNullCurrentPage_IsNotChangedByStatusEndpoint()
    {
        // 7.3 K5: Read -> Read hiçbir şeyi değiştirmez; eski boş kayıt status endpoint'iyle "düzeltilmez"
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: null, pageCount: 166);
        var before = UserBookSnapshot.From(record);

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Read));

        Assert.Equal(before, UserBookSnapshot.From(record));
        Assert.Null(record.CurrentPage);
    }

    // ---------- K2: Read -> Reading ----------

    [Fact]
    public async Task PageRules_ReadToReading_ResetsPageToZero_AndKeepsStartedAtRatingReview()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        var originalStart = record.StartedAt;
        Assert.NotNull(originalStart);
        Assert.True(record.IsFavorite);

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(ReadingStatus.Reading, result.Status);
        Assert.Equal(0, result.CurrentPage);
        Assert.Equal(0, record.CurrentPage);
        Assert.Equal(0d, result.ProgressPercentage);

        // 7.3 kuralları: başlangıç tarihi korunur, bitiş tarihi ve favori silinir, puan/yorum korunur
        Assert.Equal(originalStart, record.StartedAt);
        Assert.Null(record.FinishedYear);
        Assert.Null(record.FinishedMonth);
        Assert.Null(record.FinishedDay);
        Assert.False(record.IsFavorite);
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task PageRules_ReadToReading_WithNullStartedAt_KeepsStartedAtNull_AndResetsPage()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        record.StartedAt = null;

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(0, record.CurrentPage);
        Assert.Null(record.StartedAt); // sistem tarih uydurmaz (7.3)
    }

    [Theory]
    [InlineData(ReadingStatus.Reading, 40)]
    [InlineData(ReadingStatus.DidNotFinish, 150)]
    [InlineData(ReadingStatus.NotStarted, null)]
    [InlineData(ReadingStatus.NextUp, 20)]
    public async Task PageRules_OtherSourcesToReading_KeepCurrentPage(ReadingStatus from, int? currentPage)
    {
        var (service, record) = CreatePageBook(from, currentPage, pageCount: 300);

        await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.Reading));

        Assert.Equal(ReadingStatus.Reading, record.Status);
        Assert.Equal(currentPage, record.CurrentPage);
    }

    // ---------- Ek geçişler: Read'den çıkış ----------

    [Theory]
    [InlineData(ReadingStatus.NotStarted, 300)]
    [InlineData(ReadingStatus.NextUp, 300)]
    [InlineData(ReadingStatus.NotStarted, null)]
    [InlineData(ReadingStatus.NextUp, null)]
    public async Task PageRules_ReadToNotStartedOrNextUp_ResetsPageToZero_AndClearsStartedAt(ReadingStatus to, int? pageCount)
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 120, pageCount: pageCount);

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(to));

        Assert.Equal(to, result.Status);
        Assert.Equal(0, record.CurrentPage);
        // 7.3 kuralları aynen geçerli
        Assert.Null(record.StartedAt);
        Assert.Null(record.FinishedYear);
        Assert.False(record.IsFavorite);
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task PageRules_ReadToDidNotFinish_SetsPageToNull_AndKeepsOtherRules()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        var originalStart = record.StartedAt;

        var result = await service.UpdateStatusAsync(1, 1, TrStatus(ReadingStatus.DidNotFinish));

        Assert.Equal(ReadingStatus.DidNotFinish, result.Status);
        Assert.Null(record.CurrentPage);
        Assert.Null(result.ProgressPercentage);
        Assert.NotNull(record.DidNotFinishAt);
        Assert.Equal(originalStart, record.StartedAt);
        Assert.Null(record.FinishedYear);
        Assert.False(record.IsFavorite);
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    // ---------- #2: Read'de ilerleme koruması (PageCount biliniyor) ----------

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(299)]
    [InlineData(-5)]
    public async Task PageRules_ReadBook_ProgressBelowPageCount_Throws_AndPreservesEntity(int page)
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = page }));

        Assert.Contains("Okunuyor", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task PageRules_ReadBook_ProgressNull_Throws_AndPreservesEntity()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = null }));

        Assert.Contains("Okunuyor", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task PageRules_ReadBook_ProgressAbovePageCount_Throws_AndPreservesEntity()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);
        var before = UserBookSnapshot.From(record);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 301 }));

        Assert.Contains("aşamaz", ex.Message);
        Assert.Equal(before, UserBookSnapshot.From(record));
    }

    [Fact]
    public async Task PageRules_ReadBook_ProgressEqualToPageCount_Succeeds_AndKeepsDatesAndStatus()
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: 300);

        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 300 });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(300, record.CurrentPage);
        Assert.Equal(2024, record.FinishedYear);
        Assert.Equal(3, record.FinishedMonth);
        Assert.Equal(5, record.FinishedDay);
        Assert.True(record.IsFavorite);
    }

    [Fact]
    public async Task PageRules_ReadBook_LegacyNullCurrentPage_IsFixedByEqualProgress()
    {
        // Eski, tutarsız kayıt: Read ama CurrentPage boş. PageCount'a eşit değer gönderilince düzelir.
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: null, pageCount: 166);

        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 166 });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(166, record.CurrentPage);
        Assert.Equal(2024, record.FinishedYear);
    }

    // ---------- #2: PageCount bilinmiyorsa mevcut davranış korunur (yeni 400 kuralı yok) ----------

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task PageRules_ReadBook_WithUnknownPageCount_ProgressBehaviorIsUnchanged(int? pageCount)
    {
        var (service, record) = CreatePageBook(ReadingStatus.Read, currentPage: 300, pageCount: pageCount);

        // Herhangi bir sayfa değeri kabul edilir
        var lower = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = 50 });
        Assert.Equal(ReadingStatus.Read, lower.Status);
        Assert.Equal(50, record.CurrentPage);

        // null ilerlemeyi temizler
        var cleared = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest { CurrentPage = null });
        Assert.Equal(ReadingStatus.Read, cleared.Status);
        Assert.Null(record.CurrentPage);
    }
}