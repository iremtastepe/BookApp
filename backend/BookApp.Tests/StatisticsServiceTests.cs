using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookApp.Application.DTOs.Statistics;
using BookApp.Application.Interfaces;
using BookApp.Application.Services;
using BookApp.Domain.Enums;
using Xunit;

namespace BookApp.Tests;

public class StatisticsServiceTests
{
    // -------------------------------------------------------------------
    // 1️⃣ Test: Two different genres with the same count are both returned
    // -------------------------------------------------------------------
    [Fact]
    public async Task GetOverviewAsync_TopGenres_Tie_ReturnsBothLeaders()
    {
        // Arrange – repository returns two genres with the same highest count.
        var repo = new FakeStatisticsRepository
        {
            StatusCounts = new Dictionary<ReadingStatus, int>
            {
                [ReadingStatus.NotStarted] = 0,
                [ReadingStatus.Reading] = 0,
                [ReadingStatus.NextUp] = 0,
                [ReadingStatus.Read] = 0,
                [ReadingStatus.DidNotFinish] = 0
            },
            ReadGenreCounts = new List<NameCountDto>
            {
                new() { Name = "Fantastik", Count = 7 },
                new() { Name = "BilimKurgu", Count = 7 },
                new() { Name = "Romantik", Count = 3 }
            }
        };

        var service = new StatisticsService(repo);

        // Act
        var result = await service.GetOverviewAsync(userId: 1);

                // Assert – both tied genres must appear (order alphabetical because counts equal)
        Assert.Contains(result.TopGenres, g => g.Name == "BilimKurgu" && g.Count == 7);
        Assert.Contains(result.TopGenres, g => g.Name == "Fantastik" && g.Count == 7);
        // Romantik should also be present because we take up to TopCount = 5
        Assert.Contains(result.TopGenres, g => g.Name == "Romantik" && g.Count == 3);
        // All three should be returned because Take(TopCount) includes them
        Assert.Equal(3, result.TopGenres.Count);
    }

    // -------------------------------------------------------------------
    // 2️⃣ Test: Turkish case‑insensitive grouping (Işık vs ışık)
    // -------------------------------------------------------------------
    [Fact]
    public async Task GetOverviewAsync_TurkishCaseInsensitive_MergesNamesCorrectly()
    {
        var repo = new FakeStatisticsRepository
        {
            StatusCounts = new Dictionary<ReadingStatus, int>(),
            ReadAuthorCounts = new List<NameCountDto>
            {
                new() { Name = "Işık", Count = 3 },
                new() { Name = "ışık", Count = 2 },
                new() { Name = "Ayşe", Count = 4 }
            }
        };

        var service = new StatisticsService(repo);
        var result = await service.GetOverviewAsync(userId: 1);

        // After grouping, Işık and ışık become a single entry with total 5.
        var combined = Assert.Single(result.TopAuthors, a => a.Name == "Işık");
        Assert.Equal(5, combined.Count);
        // The other author stays separate.
        Assert.Contains(result.TopAuthors, a => a.Name == "Ayşe" && a.Count == 4);
    }

    // -------------------------------------------------------------------
    // 3️⃣ Test: All reading statuses present with distinct non‑zero counts
    // -------------------------------------------------------------------
    [Fact]
    public async Task GetOverviewAsync_StatusCounts_AllStatusesPresentWithDifferentCounts()
    {
        var repo = new FakeStatisticsRepository
        {
            StatusCounts = new Dictionary<ReadingStatus, int>
            {
                [ReadingStatus.NotStarted]    = 1,
                [ReadingStatus.Reading]       = 2,
                [ReadingStatus.NextUp]        = 3,
                [ReadingStatus.Read]          = 4,
                [ReadingStatus.DidNotFinish] = 5
            }
        };

        var service = new StatisticsService(repo);
        var result = await service.GetOverviewAsync(userId: 1);

        // Verify that each enum value appears exactly once with the correct count.
        var dict = result.StatusCounts.ToDictionary(sc => sc.Status, sc => sc.Count);
        Assert.Equal(5, dict.Count);
        Assert.Equal(1, dict[ReadingStatus.NotStarted]);
        Assert.Equal(2, dict[ReadingStatus.Reading]);
        Assert.Equal(3, dict[ReadingStatus.NextUp]);
        Assert.Equal(4, dict[ReadingStatus.Read]);
        Assert.Equal(5, dict[ReadingStatus.DidNotFinish]);
    }

    // -------------------------------------------------------------------
    // 4️⃣ Test: Monthly – year (2000 .. this year + 1) and month (1..12) boundaries
    // -------------------------------------------------------------------
    [Fact]
    public async Task GetMonthlyAsync_YearAndMonthBoundaries_AreValidated()
    {
        var service = new StatisticsService(new FakeStatisticsRepository());
        var maxYear = DateTime.UtcNow.Year + 1;

        // Geçerli sınırlar
        await service.GetMonthlyAsync(1, 2000, 1);
        await service.GetMonthlyAsync(1, maxYear, 12);

        // Geçersiz yıllar
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 1999, 6));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, maxYear + 1, 6));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 1800, 6));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 3000, 6));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 0, 6));

        // Geçersiz aylar
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 2026, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlyAsync(1, 2026, 13));
    }

    // -------------------------------------------------------------------
    // 5️⃣ Test: Yearly – year (2000 .. this year + 1) boundaries
    // -------------------------------------------------------------------
    [Fact]
    public async Task GetYearlyAsync_YearBoundaries_AreValidated()
    {
        var service = new StatisticsService(new FakeStatisticsRepository());
        var maxYear = DateTime.UtcNow.Year + 1;

        // Geçerli sınırlar
        var first = await service.GetYearlyAsync(1, 2000);
        Assert.Equal(2000, first.Year);
        Assert.Equal(12, first.Months.Count); // boş yıl da 12 ay sıfırla döner

        var last = await service.GetYearlyAsync(1, maxYear);
        Assert.Equal(maxYear, last.Year);

        // Geçersiz yıllar
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetYearlyAsync(1, 1999));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetYearlyAsync(1, maxYear + 1));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetYearlyAsync(1, 1800));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetYearlyAsync(1, 3000));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetYearlyAsync(1, 0));
    }

    // -------------------------------------------------------------------
    // Helper fake repository – implements every method of IStatisticsRepository.
    // -------------------------------------------------------------------
    private class FakeStatisticsRepository : IStatisticsRepository
    {
        // Data that individual tests can set
        public Dictionary<ReadingStatus, int> StatusCounts { get; set; } = new();
        public int FavoriteCount { get; set; }
        public List<NameCountDto> ReadAuthorCounts { get; set; } = new();
        public List<NameCountDto> ReadGenreCounts { get; set; } = new();
        public int ReadGenreMissingCount { get; set; }
        public List<RatedBookDto> TopRatedReadBooks { get; set; } = new();
        public List<RatingBucketDto> ReadRatingCounts { get; set; } = new();
        public List<YearCountDto> YearCounts { get; set; } = new();
        public List<MonthCountDto> MonthCounts { get; set; } = new();
        public int UnknownMonthCount { get; set; }
        public int UndatedReadCount { get; set; }
        public List<RatedBookDto> RatedReadBooks { get; set; } = new();

        public Task<Dictionary<ReadingStatus, int>> GetStatusCountsAsync(int userId) =>
            Task.FromResult(StatusCounts);

        public Task<int> GetFavoriteCountAsync(int userId) => Task.FromResult(FavoriteCount);

        public Task<int> GetReadCountAsync(int userId, int? year = null, int? month = null) =>
            Task.FromResult(0);

        public Task<List<NameCountDto>> GetReadAuthorCountsAsync(int userId, int? year = null, int? month = null) =>
            Task.FromResult(ReadAuthorCounts);

        public Task<List<NameCountDto>> GetReadGenreCountsAsync(int userId, int? year = null, int? month = null) =>
            Task.FromResult(ReadGenreCounts);

        public Task<int> GetReadGenreMissingCountAsync(int userId, int? year = null, int? month = null) =>
            Task.FromResult(ReadGenreMissingCount);

        public Task<List<RatedBookDto>> GetTopRatedReadBooksAsync(int userId, int take) =>
            Task.FromResult(TopRatedReadBooks);

        public Task<List<RatingBucketDto>> GetReadRatingCountsAsync(int userId) =>
            Task.FromResult(ReadRatingCounts);

        public Task<List<YearCountDto>> GetReadCountsByYearAsync(int userId) =>
            Task.FromResult(YearCounts);

        public Task<List<MonthCountDto>> GetReadCountsByMonthAsync(int userId, int year) =>
            Task.FromResult(MonthCounts);

        public Task<int> GetReadUnknownMonthCountAsync(int userId, int year) =>
            Task.FromResult(UnknownMonthCount);

        public Task<int> GetUndatedReadCountAsync(int userId) =>
            Task.FromResult(UndatedReadCount);

        public Task<List<RatedBookDto>> GetRatedReadBooksAsync(int userId, int year, int month) =>
            Task.FromResult(RatedReadBooks);
    }
}