using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
 
namespace BookApp.Tests;
 
// 7.1 - Kitap arşivleme: Adım 3 (liste filtresi).
// Bu aşamada arşivleme endpoint'i yok; arşivli kayıtlar AppDbContext üzerinden doğrudan ArchivedAt yazılarak hazırlanır.
// Gerçek controller, servis ve repository çalışır. InMemory sağlayıcı kullanıldığı için ILike (Search) ve
// Collate (Title sıralaması) bu dosyada kullanılmaz.
public class ArchiveUserBookTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
 
    public ArchiveUserBookTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }
 
    // Mevcut test sınıflarındaki yardımcının rolsüz, kısaltılmış kopyası (mevcut 196 testin riske girmemesi için bilinçli tekrar).
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
 
    // Yeni bir katalog kitabı oluşturur ve kullanıcının kitaplığına ekler. (UserBookId, BookId) döner.
    private async Task<(int UserBookId, int BookId)> CreateBookAndAddToLibraryAsync(HttpClient client, string label)
    {
        var createRes = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = $"{label} {Guid.NewGuid():N}",
            Author = "Arsiv Test Yazari",
            PageCount = 200
        });
        createRes.EnsureSuccessStatusCode();
        var book = await createRes.Content.ReadFromJsonAsync<BookResponse>();
 
        var userBookId = await AddExistingBookToLibraryAsync(client, book!.Id);
        return (userBookId, book.Id);
    }
 
    private async Task<int> AddExistingBookToLibraryAsync(HttpClient client, int bookId)
    {
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = bookId });
        addRes.EnsureSuccessStatusCode();
        var userBook = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
        return userBook!.Id;
    }
 
    // Veritabanındaki kaydı doğrudan değiştirir (arşivleme endpoint'i henüz yok).
    private async Task MutateUserBookDirectAsync(int userBookId, Action<UserBook> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userBook = await db.UserBooks.FirstAsync(x => x.Id == userBookId);
        mutate(userBook);
        await db.SaveChangesAsync();
    }
 
    private Task ArchiveDirectAsync(int userBookId)
        => MutateUserBookDirectAsync(userBookId, ub => ub.ArchivedAt = DateTime.UtcNow);
 
    private async Task<PagedResponse<UserBookDto>> GetLibraryAsync(HttpClient client, string queryString = "")
    {
        var res = await client.GetAsync("/api/library" + queryString);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var paged = await res.Content.ReadFromJsonAsync<PagedResponse<UserBookDto>>();
        Assert.NotNull(paged);
        return paged;
    }
 
    private static List<int> SortedIds(PagedResponse<UserBookDto> paged)
        => paged.Items.Select(i => i.Id).OrderBy(i => i).ToList();
 
    private static List<int> Sorted(params int[] ids)
        => ids.OrderBy(i => i).ToList();
 
    [Fact]
    public async Task DefaultList_HidesArchivedBooks()
    {
        var client = await CreateUserClientAsync("arch_default@test.com");
        var (active1, _) = await CreateBookAndAddToLibraryAsync(client, "Aktif 1");
        var (active2, _) = await CreateBookAndAddToLibraryAsync(client, "Aktif 2");
        var (archived, _) = await CreateBookAndAddToLibraryAsync(client, "Arsivli");
        await ArchiveDirectAsync(archived);
 
        var result = await GetLibraryAsync(client);
 
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(Sorted(active1, active2), SortedIds(result));
        Assert.DoesNotContain(result.Items, i => i.Id == archived);
    }
 
    [Fact]
    public async Task ArchivedTrue_ReturnsOnlyArchivedBooks()
    {
        var client = await CreateUserClientAsync("arch_true@test.com");
        var (active, _) = await CreateBookAndAddToLibraryAsync(client, "Aktif");
        var (archived1, _) = await CreateBookAndAddToLibraryAsync(client, "Arsivli 1");
        var (archived2, _) = await CreateBookAndAddToLibraryAsync(client, "Arsivli 2");
        await ArchiveDirectAsync(archived1);
        await ArchiveDirectAsync(archived2);
 
        var result = await GetLibraryAsync(client, "?archived=true");
 
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(Sorted(archived1, archived2), SortedIds(result));
        Assert.DoesNotContain(result.Items, i => i.Id == active);
    }
 
    [Fact]
    public async Task ArchivedFalseExplicit_BehavesLikeDefault()
    {
        var client = await CreateUserClientAsync("arch_false@test.com");
        var (active, _) = await CreateBookAndAddToLibraryAsync(client, "Aktif");
        var (archived, _) = await CreateBookAndAddToLibraryAsync(client, "Arsivli");
        await ArchiveDirectAsync(archived);
 
        var defaultResult = await GetLibraryAsync(client);
        var explicitResult = await GetLibraryAsync(client, "?archived=false");
 
        Assert.Equal(defaultResult.TotalCount, explicitResult.TotalCount);
        Assert.Equal(SortedIds(defaultResult), SortedIds(explicitResult));
        Assert.Equal(Sorted(active), SortedIds(explicitResult));
    }
 
    [Fact]
    public async Task ArchivedFilter_WorksTogetherWithStatusFilter()
    {
        var client = await CreateUserClientAsync("arch_status@test.com");
        var (readArchived, _) = await CreateBookAndAddToLibraryAsync(client, "Read Arsivli");
        var (readActive, _) = await CreateBookAndAddToLibraryAsync(client, "Read Aktif");
        var (readingArchived, _) = await CreateBookAndAddToLibraryAsync(client, "Reading Arsivli");
 
        await MutateUserBookDirectAsync(readArchived, ub => { ub.Status = ReadingStatus.Read; ub.ArchivedAt = DateTime.UtcNow; });
        await MutateUserBookDirectAsync(readActive, ub => ub.Status = ReadingStatus.Read);
        await MutateUserBookDirectAsync(readingArchived, ub => { ub.Status = ReadingStatus.Reading; ub.ArchivedAt = DateTime.UtcNow; });
 
        var archivedRead = await GetLibraryAsync(client, "?archived=true&status=Read");
        Assert.Equal(1, archivedRead.TotalCount);
        Assert.Equal(Sorted(readArchived), SortedIds(archivedRead));
 
        var activeRead = await GetLibraryAsync(client, "?archived=false&status=Read");
        Assert.Equal(1, activeRead.TotalCount);
        Assert.Equal(Sorted(readActive), SortedIds(activeRead));
 
        var archivedReadOrReading = await GetLibraryAsync(client, "?archived=true&status=Read&status=Reading");
        Assert.Equal(2, archivedReadOrReading.TotalCount);
        Assert.Equal(Sorted(readArchived, readingArchived), SortedIds(archivedReadOrReading));
    }
 
    [Fact]
    public async Task ArchivedFilter_WorksTogetherWithFavoriteFilter()
    {
        var client = await CreateUserClientAsync("arch_fav@test.com");
        var (favArchived, _) = await CreateBookAndAddToLibraryAsync(client, "Favori Arsivli");
        var (favActive, _) = await CreateBookAndAddToLibraryAsync(client, "Favori Aktif");
        var (plainArchived, _) = await CreateBookAndAddToLibraryAsync(client, "Siradan Arsivli");
 
        await MutateUserBookDirectAsync(favArchived, ub => { ub.Status = ReadingStatus.Read; ub.IsFavorite = true; ub.ArchivedAt = DateTime.UtcNow; });
        await MutateUserBookDirectAsync(favActive, ub => { ub.Status = ReadingStatus.Read; ub.IsFavorite = true; });
        await MutateUserBookDirectAsync(plainArchived, ub => ub.ArchivedAt = DateTime.UtcNow);
 
        var archivedFavorites = await GetLibraryAsync(client, "?archived=true&isFavorite=true");
        Assert.Equal(1, archivedFavorites.TotalCount);
        Assert.Equal(Sorted(favArchived), SortedIds(archivedFavorites));
 
        var archivedNonFavorites = await GetLibraryAsync(client, "?archived=true&isFavorite=false");
        Assert.Equal(1, archivedNonFavorites.TotalCount);
        Assert.Equal(Sorted(plainArchived), SortedIds(archivedNonFavorites));
 
        var activeFavorites = await GetLibraryAsync(client, "?archived=false&isFavorite=true");
        Assert.Equal(1, activeFavorites.TotalCount);
        Assert.Equal(Sorted(favActive), SortedIds(activeFavorites));
    }
 
    [Fact]
    public async Task ArchivedList_TotalCountAndPaging_AreCorrect()
    {
        var client = await CreateUserClientAsync("arch_paging@test.com");
 
        var archivedIds = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            var (id, _) = await CreateBookAndAddToLibraryAsync(client, $"Arsivli {i}");
            await ArchiveDirectAsync(id);
            archivedIds.Add(id);
        }
 
        var activeIds = new List<int>();
        for (var i = 1; i <= 2; i++)
        {
            var (id, _) = await CreateBookAndAddToLibraryAsync(client, $"Aktif {i}");
            activeIds.Add(id);
        }
 
        var page1 = await GetLibraryAsync(client, "?archived=true&page=1&pageSize=2");
        var page2 = await GetLibraryAsync(client, "?archived=true&page=2&pageSize=2");
        var page3 = await GetLibraryAsync(client, "?archived=true&page=3&pageSize=2");
 
        // totalCount sayfadan bağımsız ve yalnızca arşivlileri sayar (aktif 2 kitap dahil değildir)
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(5, page3.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
 
        // Sayfalar arasında tekrar veya eksik kayıt olmamalı
        var allPagedIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.Id).ToList();
        Assert.Equal(5, allPagedIds.Distinct().Count());
        Assert.Equal(archivedIds.OrderBy(i => i).ToList(), allPagedIds.OrderBy(i => i).ToList());
 
        // Aktif liste etkilenmez
        var activeResult = await GetLibraryAsync(client);
        Assert.Equal(2, activeResult.TotalCount);
        Assert.Equal(activeIds.OrderBy(i => i).ToList(), SortedIds(activeResult));
    }
 
    [Fact]
    public async Task ArchivedList_DoesNotLeakOtherUsersBooks()
    {
        var user1 = await CreateUserClientAsync("arch_iso1@test.com");
        var user2 = await CreateUserClientAsync("arch_iso2@test.com");
 
        var (user1Archived, sharedBookId) = await CreateBookAndAddToLibraryAsync(user1, "Ortak Katalog Kitabi");
        await ArchiveDirectAsync(user1Archived);
 
        // Kullanıcı 2 aynı katalog kitabını kendi kitaplığına ekler (aktif); ayrıca kendi arşivli kitabı vardır
        var user2Active = await AddExistingBookToLibraryAsync(user2, sharedBookId);
        var (user2Archived, _) = await CreateBookAndAddToLibraryAsync(user2, "Kullanici 2 Arsivli");
        await ArchiveDirectAsync(user2Archived);
 
        var user1Archive = await GetLibraryAsync(user1, "?archived=true");
        Assert.Equal(Sorted(user1Archived), SortedIds(user1Archive));
 
        var user2Archive = await GetLibraryAsync(user2, "?archived=true");
        Assert.Equal(Sorted(user2Archived), SortedIds(user2Archive));
 
        var user2Default = await GetLibraryAsync(user2);
        Assert.Equal(Sorted(user2Active), SortedIds(user2Default));
    }
 
    [Fact]
    public async Task ArchivedTrue_WithNoArchivedBooks_ReturnsEmptyList()
    {
        var client = await CreateUserClientAsync("arch_empty@test.com");
        var (active, _) = await CreateBookAndAddToLibraryAsync(client, "Sadece Aktif");
 
        var result = await GetLibraryAsync(client, "?archived=true");
 
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
 
        var defaultResult = await GetLibraryAsync(client);
        Assert.Equal(Sorted(active), SortedIds(defaultResult));
    }
 
    [Fact]
    public async Task BookReturnsToDefaultList_WhenArchivedAtIsClearedAgain()
    {
        var client = await CreateUserClientAsync("arch_restore@test.com");
        var (userBookId, _) = await CreateBookAndAddToLibraryAsync(client, "Geri Yuklenecek");
 
        await ArchiveDirectAsync(userBookId);
        Assert.Empty((await GetLibraryAsync(client)).Items);
        Assert.Single((await GetLibraryAsync(client, "?archived=true")).Items);
 
        // Geri yükleme endpoint'i henüz yok (Adım 5); ArchivedAt doğrudan null'a çekilir
        await MutateUserBookDirectAsync(userBookId, ub => ub.ArchivedAt = null);
 
        Assert.Single((await GetLibraryAsync(client)).Items);
        Assert.Empty((await GetLibraryAsync(client, "?archived=true")).Items);
    }
 
    [Fact]
    public async Task ArchivedQuery_WithInvalidValue_Returns400()
    {
        var client = await CreateUserClientAsync("arch_invalid@test.com");
 
        var res = await client.GetAsync("/api/library?archived=abc");
 
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
 
    [Fact]
    public async Task AddingArchivedBookAgain_StillReturns409AndKeepsSingleRecord()
    {
        var client = await CreateUserClientAsync("arch_readd@test.com");
        var (userBookId, bookId) = await CreateBookAndAddToLibraryAsync(client, "Tekrar Eklenecek");
        await ArchiveDirectAsync(userBookId);
 
        // ExistsAsync arşivli kayıtları da görmeye devam etmeli (filtre yalnızca liste sorgusunda)
        var addAgain = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = bookId });
        Assert.Equal(HttpStatusCode.Conflict, addAgain.StatusCode);
 
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.UserBooks.CountAsync(ub => ub.BookId == bookId);
        Assert.Equal(1, count);
    }
}
 