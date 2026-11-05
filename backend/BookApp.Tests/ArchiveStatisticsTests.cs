using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Goals;
using BookApp.Application.DTOs.Library;
using BookApp.Application.DTOs.Statistics;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
 
namespace BookApp.Tests;
 
// 7.1 - Kitap arşivleme: Adım 6 (istatistik ve hedeflerde arşivli kitaplar).
// Kural: arşivlemek hiçbir sayıyı değiştirmez; arşivli "Okundu" kitaplar yıllık hedefte, aylık listede,
// aylık/genel istatistikte yer almaya devam eder ve yalnızca archivedAt alanı ile işaretlenir.
// Tamamı gerçek HTTP istekleriyle çalışır: gerçek controller, servis, repository ve (InMemory) veritabanı.
public class ArchiveStatisticsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int Year = 2024;
    private const int Month = 5;
 
    private readonly CustomWebApplicationFactory _factory;
 
    public ArchiveStatisticsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }
 
    // Mevcut test sınıflarındaki yardımcının rolsüz, kısaltılmış kopyası.
    private async Task<HttpClient> CreateUserClientAsync(string email)
    {
        const string password = "Password123!";
 
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new User { UserName = email.Split('@')[0], Email = email };
                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    throw new Exception("Test kullanıcısı oluşturulamadı: " + string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
        }
 
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
 
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        return client;
    }
 
    // Yeni katalog kitabı oluşturur, kitaplığa ekler ve kaydı doğrudan istenen duruma getirir. UserBookId döner.
    private async Task<int> CreateUserBookAsync(HttpClient client, string label, Action<UserBook> shape)
    {
        var createRes = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = $"{label} {Guid.NewGuid():N}",
            Author = "Arsiv Istatistik Yazari",
            PageCount = 200
        });
        createRes.EnsureSuccessStatusCode();
        var book = await createRes.Content.ReadFromJsonAsync<BookResponse>();
 
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        addRes.EnsureSuccessStatusCode();
        var userBook = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
 
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.UserBooks.FirstAsync(x => x.Id == userBook!.Id);
        shape(entity);
        await db.SaveChangesAsync();
 
        return userBook!.Id;
    }
 
    // 2024 Mayıs'ta bitmiş "Okundu" kayıt.
    private static Action<UserBook> ReadInMay(int day, decimal rating, bool favorite = false) => ub =>
    {
        ub.Status = ReadingStatus.Read;
        ub.CurrentPage = 200;
        ub.FinishedYear = Year;
        ub.FinishedMonth = Month;
        ub.FinishedDay = day;
        ub.Rating = rating;
        ub.IsFavorite = favorite;
    };
 
    // İki okunmuş kitap: A (4.5 puan, favori, 20 Mayıs) arşivlenecek, B (3.0 puan, 10 Mayıs) aktif kalacak.
    private async Task<(HttpClient Client, int ArchivedBookId, int ActiveBookId)> SetUpTwoReadBooksAsync(string email)
    {
        var client = await CreateUserClientAsync(email);
        var archivedBookId = await CreateUserBookAsync(client, "Arsivlenecek", ReadInMay(20, 4.5m, favorite: true));
        var activeBookId = await CreateUserBookAsync(client, "Aktif Kalacak", ReadInMay(10, 3.0m));
        return (client, archivedBookId, activeBookId);
    }
 
    private static async Task ArchiveAsync(HttpClient client, int userBookId)
        => (await client.PostAsync($"/api/library/{userBookId}/archive", null)).EnsureSuccessStatusCode();
 
    private static async Task RestoreAsync(HttpClient client, int userBookId)
        => (await client.PostAsync($"/api/library/{userBookId}/restore", null)).EnsureSuccessStatusCode();
 
    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body;
    }
 
    // Arşivlemeden bağımsız olması gereken tüm sayıların anlık görüntüsü (record: değer eşitliği; liste yerine string).
    private sealed record NumbersSnapshot(
        int ReadBooksCount,
        int TotalReadBooks,
        double? Percentage,
        int? RemainingBooks,
        int MonthlyListTotal,
        string MonthlyListIds,
        int MonthlyStatsCompleted,
        int OverviewTotalBooks,
        int OverviewReadCount,
        int OverviewFavoriteCount,
        string OverviewTopRatedIds,
        string MonthlyHighestIds,
        string MonthlyLowestIds);
 
    private static async Task<NumbersSnapshot> TakeSnapshotAsync(HttpClient client)
    {
        var goal = await GetAsync<AnnualReadingGoalDto>(client, $"/api/readinggoals/{Year}");
        var list = await GetAsync<MonthlyReadSummaryDto>(client, $"/api/readinggoals/monthly?year={Year}&month={Month}");
        var monthly = await GetAsync<MonthlyStatisticsDto>(client, $"/api/statistics/monthly?year={Year}&month={Month}");
        var overview = await GetAsync<OverviewStatisticsDto>(client, "/api/statistics/overview");
 
        return new NumbersSnapshot(
            goal.ReadBooksCount,
            goal.TotalReadBooks,
            goal.Percentage,
            goal.RemainingBooks,
            list.TotalCount,
            string.Join(",", list.Books.Select(b => b.UserBookId)),
            monthly.CompletedCount,
            overview.TotalBooks,
            overview.StatusCounts.Single(s => s.Status == ReadingStatus.Read).Count,
            overview.FavoriteCount,
            string.Join(",", overview.TopRatedBooks.Select(b => b.UserBookId)),
            string.Join(",", monthly.HighestRatedBooks.Select(b => b.UserBookId)),
            string.Join(",", monthly.LowestRatedBooks.Select(b => b.UserBookId)));
    }
 
    [Fact]
    public async Task ArchivingReadBook_DoesNotChangeAnnualGoalProgress()
    {
        var (client, archivedId, _) = await SetUpTwoReadBooksAsync("arch6_goal@test.com");
        var put = await client.PutAsJsonAsync($"/api/readinggoals/{Year}", new CreateOrUpdateReadingGoalRequest { TargetBooks = 10 });
        put.EnsureSuccessStatusCode();
 
        var before = await GetAsync<AnnualReadingGoalDto>(client, $"/api/readinggoals/{Year}");
        Assert.Equal(2, before.ReadBooksCount);
        Assert.Equal(2, before.TotalReadBooks);
        Assert.Equal(20.0, before.Percentage);
        Assert.Equal(8, before.RemainingBooks);
 
        await ArchiveAsync(client, archivedId);
 
        var after = await GetAsync<AnnualReadingGoalDto>(client, $"/api/readinggoals/{Year}");
        Assert.Equal(before.ReadBooksCount, after.ReadBooksCount);
        Assert.Equal(before.TotalReadBooks, after.TotalReadBooks);
        Assert.Equal(before.Percentage, after.Percentage);
        Assert.Equal(before.RemainingBooks, after.RemainingBooks);
        Assert.Equal(before.TargetBooks, after.TargetBooks);
    }
 
    [Fact]
    public async Task MonthlyReadingList_KeepsArchivedBookAndMarksItWithArchivedAt()
    {
        var (client, archivedId, activeId) = await SetUpTwoReadBooksAsync("arch6_list@test.com");
 
        var before = await GetAsync<MonthlyReadSummaryDto>(client, $"/api/readinggoals/monthly?year={Year}&month={Month}");
        Assert.Equal(2, before.TotalCount);
        Assert.All(before.Books, b => Assert.Null(b.ArchivedAt));
 
        await ArchiveAsync(client, archivedId);
 
        var after = await GetAsync<MonthlyReadSummaryDto>(client, $"/api/readinggoals/monthly?year={Year}&month={Month}");
        Assert.Equal(2, after.TotalCount);
        Assert.Equal(before.Books.Select(b => b.UserBookId), after.Books.Select(b => b.UserBookId));
        Assert.NotNull(after.Books.Single(b => b.UserBookId == archivedId).ArchivedAt);
        Assert.Null(after.Books.Single(b => b.UserBookId == activeId).ArchivedAt);
    }
 
    [Fact]
    public async Task Overview_StillCountsArchivedBooks_AndMarksArchivedTopRatedBook()
    {
        var (client, archivedId, activeId) = await SetUpTwoReadBooksAsync("arch6_overview@test.com");
        var readingId = await CreateUserBookAsync(client, "Okunuyor", ub =>
        {
            ub.Status = ReadingStatus.Reading;
            ub.CurrentPage = 50;
        });
 
        var before = await GetAsync<OverviewStatisticsDto>(client, "/api/statistics/overview");
        Assert.Equal(3, before.TotalBooks);
        Assert.Equal(2, before.StatusCounts.Single(s => s.Status == ReadingStatus.Read).Count);
        Assert.Equal(1, before.StatusCounts.Single(s => s.Status == ReadingStatus.Reading).Count);
        Assert.Equal(1, before.FavoriteCount);
 
        await ArchiveAsync(client, archivedId);
        await ArchiveAsync(client, readingId);
 
        var after = await GetAsync<OverviewStatisticsDto>(client, "/api/statistics/overview");
        Assert.Equal(before.TotalBooks, after.TotalBooks);
        Assert.Equal(2, after.StatusCounts.Single(s => s.Status == ReadingStatus.Read).Count);
        Assert.Equal(1, after.StatusCounts.Single(s => s.Status == ReadingStatus.Reading).Count);
        Assert.Equal(before.FavoriteCount, after.FavoriteCount);
 
        // En yüksek puanlılar: aynı kitaplar, aynı sıra; arşivli olan işaretli, aktif olan işaretsiz
        Assert.Equal(before.TopRatedBooks.Select(b => b.UserBookId), after.TopRatedBooks.Select(b => b.UserBookId));
        Assert.Equal(archivedId, after.TopRatedBooks[0].UserBookId);
        Assert.NotNull(after.TopRatedBooks.Single(b => b.UserBookId == archivedId).ArchivedAt);
        Assert.Null(after.TopRatedBooks.Single(b => b.UserBookId == activeId).ArchivedAt);
    }
 
    [Fact]
    public async Task MonthlyStatistics_StillCountsArchivedBook_AndMarksRatedBooks()
    {
        var (client, archivedId, activeId) = await SetUpTwoReadBooksAsync("arch6_monthly@test.com");
 
        var before = await GetAsync<MonthlyStatisticsDto>(client, $"/api/statistics/monthly?year={Year}&month={Month}");
        Assert.Equal(2, before.CompletedCount);
 
        await ArchiveAsync(client, archivedId);
 
        var after = await GetAsync<MonthlyStatisticsDto>(client, $"/api/statistics/monthly?year={Year}&month={Month}");
        Assert.Equal(before.CompletedCount, after.CompletedCount);
 
        // Arşivli kitap 4.5 ile ayın en yüksek puanlısı olarak kalır, aktif kitap 3.0 ile en düşük puanlı olarak kalır
        var highest = Assert.Single(after.HighestRatedBooks);
        Assert.Equal(archivedId, highest.UserBookId);
        Assert.NotNull(highest.ArchivedAt);
 
        var lowest = Assert.Single(after.LowestRatedBooks);
        Assert.Equal(activeId, lowest.UserBookId);
        Assert.Null(lowest.ArchivedAt);
    }
 
    [Fact]
    public async Task ArchiveThenRestore_KeepsEveryNumberIdentical_AndClearsArchivedAtEverywhere()
    {
        var (client, archivedId, _) = await SetUpTwoReadBooksAsync("arch6_cycle@test.com");
        (await client.PutAsJsonAsync($"/api/readinggoals/{Year}", new CreateOrUpdateReadingGoalRequest { TargetBooks = 10 }))
            .EnsureSuccessStatusCode();
 
        var before = await TakeSnapshotAsync(client);
 
        await ArchiveAsync(client, archivedId);
        Assert.Equal(before, await TakeSnapshotAsync(client));
 
        await RestoreAsync(client, archivedId);
        Assert.Equal(before, await TakeSnapshotAsync(client));
 
        var list = await GetAsync<MonthlyReadSummaryDto>(client, $"/api/readinggoals/monthly?year={Year}&month={Month}");
        Assert.All(list.Books, b => Assert.Null(b.ArchivedAt));
        var overview = await GetAsync<OverviewStatisticsDto>(client, "/api/statistics/overview");
        Assert.All(overview.TopRatedBooks, b => Assert.Null(b.ArchivedAt));
        var monthly = await GetAsync<MonthlyStatisticsDto>(client, $"/api/statistics/monthly?year={Year}&month={Month}");
        Assert.All(monthly.HighestRatedBooks.Concat(monthly.LowestRatedBooks), b => Assert.Null(b.ArchivedAt));
    }
}
 