using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookApp.Tests;

// 7.3: Durum geçişlerinin HTTP üzerinden uçtan uca doğrulanması (Swagger senaryosunun otomatik hâli).
public class LibraryStatusTransitionEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LibraryStatusTransitionEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new User { UserName = email.Split('@')[0], Email = email };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new Exception("Test kullanıcısı oluşturulamadı: " + string.Join(", ", createResult.Errors.Select(e => e.Description)));
        }

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        return client;
    }

    private static async Task<UserBookDto> AddBookToLibraryAsync(HttpClient client, string title)
    {
        var createBookResponse = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = title,
            Author = "Author",
            PageCount = 200
        });
        createBookResponse.EnsureSuccessStatusCode();
        var book = await createBookResponse.Content.ReadFromJsonAsync<BookResponse>();

        var addLibResponse = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        addLibResponse.EnsureSuccessStatusCode();
        return (await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>())!;
    }

    private static UpdateUserBookStatusRequest ReadOn(int year, int month, int day)
        => new()
        {
            Status = ReadingStatus.Read,
            FinishedDate = new CompletionDateDto { Year = year, Month = month, Day = day }
        };

    [Fact]
    public async Task ReadToRead_SameDate_Returns200_DifferentDate_Returns400WithHint_AndDateIsKept()
    {
        var client = await CreateAuthorizedClientAsync("user_readread@test.com", "Password123!");
        var userBook = await AddBookToLibraryAsync(client, "ReadRead Book");

        // NotStarted -> Read (1 Kasım 2024)
        var first = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(2024, 11, 1));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Aynı tarihle tekrar Read -> 200, değişmez
        var same = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(2024, 11, 1));
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        var sameBody = await same.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(2024, sameBody!.FinishedYear);
        Assert.Equal(11, sameBody.FinishedMonth);
        Assert.Equal(1, sameBody.FinishedDay);

        // Farklı tarihle Read (5 Kasım) -> 400 ve yönlendirme mesajı
        var different = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(2024, 11, 5));
        Assert.Equal(HttpStatusCode.BadRequest, different.StatusCode);
        Assert.Contains("finished-date", await different.Content.ReadAsStringAsync());

        // Tarih bozulmadı; doğru yol olan finished-date endpoint'i ile değişir
        var change = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/finished-date", new UpdateFinishedDateRequest
        {
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 11, Day = 5 }
        });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var changed = await change.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(5, changed!.FinishedDay);
    }

    [Fact]
    public async Task PastReadBook_ReadingThenReadAgain_DoesNotFabricateStartedAt_AndAcceptsOldFinishDate()
    {
        var client = await CreateAuthorizedClientAsync("user_trap@test.com", "Password123!");
        var userBook = await AddBookToLibraryAsync(client, "Trap Book");

        // Geçmişte bitmiş kitap: StartedAt boş
        var read = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(2024, 3, 5));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // Read -> Reading: bitiş tarihi temizlenir, StartedAt uydurulmaz
        var reading = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading });
        Assert.Equal(HttpStatusCode.OK, reading.StatusCode);
        var readingBody = await reading.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Null(readingBody!.StartedAt);
        Assert.Null(readingBody.FinishedYear);

        // Tekrar Read (2024 tarihi): reddedilmemeli
        var readAgain = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(2024, 6, 1));
        Assert.Equal(HttpStatusCode.OK, readAgain.StatusCode);
        var readAgainBody = await readAgain.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.Read, readAgainBody!.Status);
        Assert.Null(readAgainBody.StartedAt);
        Assert.Equal(6, readAgainBody.FinishedMonth);
    }

    [Fact]
    public async Task BackToNextUp_ClearsStartedAtAndFinishedDate()
    {
        var client = await CreateAuthorizedClientAsync("user_nextup@test.com", "Password123!");
        var userBook = await AddBookToLibraryAsync(client, "NextUp Book");

        var reading = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading });
        reading.EnsureSuccessStatusCode();
        var readingBody = await reading.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.NotNull(readingBody!.StartedAt);

        var nextUp = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", new UpdateUserBookStatusRequest { Status = ReadingStatus.NextUp });
        Assert.Equal(HttpStatusCode.OK, nextUp.StatusCode);
        var nextUpBody = await nextUp.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.NextUp, nextUpBody!.Status);
        Assert.Null(nextUpBody.StartedAt);
        Assert.Null(nextUpBody.FinishedYear);
    }

    // ---------- 7.4: sayfa sayısı / CurrentPage / durum tutarlılığı ----------

    [Fact]
    public async Task ReadToReading_ResetsPageToZero_KeepsStartedAt_ClearsFavorite_AndFinishedDate()
    {
        var client = await CreateAuthorizedClientAsync("user_pagereset@test.com", "Password123!");
        var userBook = await AddBookToLibraryAsync(client, "PageReset Book");

        // 120. sayfaya ilerle -> otomatik Reading, StartedAt atanır
        var progress = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/progress", new UpdateProgressRequest { CurrentPage = 120 });
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        var progressBody = await progress.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.Reading, progressBody!.Status);
        var originalStart = progressBody.StartedAt;
        Assert.NotNull(originalStart);

        // Reading -> Read (bugünün tarihi): K1 gereği CurrentPage toplam sayfaya (200) eşitlenir
        var today = DateTime.UtcNow.Date;
        var read = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", ReadOn(today.Year, today.Month, today.Day));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.Read, readBody!.Status);
        Assert.Equal(200, readBody.CurrentPage);
        Assert.Equal(100d, readBody.ProgressPercentage);

        var favorite = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/favorite", new SetFavoriteRequest { IsFavorite = true });
        Assert.Equal(HttpStatusCode.OK, favorite.StatusCode);

        // Read -> Reading: K2 gereği sayfa 0, başlangıç tarihi aynı, favori ve bitiş tarihi silinir
        var reading = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading });
        Assert.Equal(HttpStatusCode.OK, reading.StatusCode);
        var readingBody = await reading.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.Reading, readingBody!.Status);
        Assert.Equal(0, readingBody.CurrentPage);
        Assert.Equal(originalStart, readingBody.StartedAt);
        Assert.Null(readingBody.FinishedYear);
        Assert.False(readingBody.IsFavorite);
    }

    [Fact]
    public async Task ReadBook_ProgressRules_LowerAndNullReturn400_EqualToPageCountReturns200()
    {
        var client = await CreateAuthorizedClientAsync("user_readprogress@test.com", "Password123!");
        var userBook = await AddBookToLibraryAsync(client, "ReadProgress Book");

        // NotStarted -> Read (tarih bilinmiyor): PageCount (200) biliniyor, CurrentPage 200 olur
        var read = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(200, readBody!.CurrentPage);

        // Toplam sayfadan düşük değer -> 400, açıklayıcı mesaj
        var lower = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/progress", new UpdateProgressRequest { CurrentPage = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
        Assert.Contains("Okunuyor", await lower.Content.ReadAsStringAsync());

        // Toplam sayfadan büyük değer -> 400
        var higher = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/progress", new UpdateProgressRequest { CurrentPage = 201 });
        Assert.Equal(HttpStatusCode.BadRequest, higher.StatusCode);

        // null (ilerlemeyi temizle) -> 400
        var nullContent = new StringContent("{\"currentPage\": null}", System.Text.Encoding.UTF8, "application/json");
        var cleared = await client.PutAsync($"/api/library/{userBook.Id}/progress", nullContent);
        Assert.Equal(HttpStatusCode.BadRequest, cleared.StatusCode);

        // Toplam sayfaya eşit değer -> 200, kayıt Read olarak kalır
        var equal = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/progress", new UpdateProgressRequest { CurrentPage = 200 });
        Assert.Equal(HttpStatusCode.OK, equal.StatusCode);
        var equalBody = await equal.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(ReadingStatus.Read, equalBody!.Status);
        Assert.Equal(200, equalBody.CurrentPage);
    }
}