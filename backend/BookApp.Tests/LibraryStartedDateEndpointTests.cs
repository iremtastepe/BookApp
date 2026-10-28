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

public class LibraryStartedDateEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LibraryStartedDateEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, User User, string Token)> CreateUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
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

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        return (client, user, authResult.Token);
    }

    [Fact]
    public async Task PutStartedDate_WithoutToken_Returns401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/library/1/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 1 }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutStartedDate_OtherUsersBook_Returns404NotFound()
    {
        var (clientUser1, _, _) = await CreateUserAsync("user1_sd@test.com", "Password123!");
        var (clientUser2, _, _) = await CreateUserAsync("user2_sd@test.com", "Password123!");

        // User1 bir kitap ekler
        var createBookResponse = await clientUser1.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "User1 Book",
            Author = "Author 1",
            PageCount = 200
        });
        createBookResponse.EnsureSuccessStatusCode();
        var book = await createBookResponse.Content.ReadFromJsonAsync<BookResponse>();

        var addLibResponse = await clientUser1.PostAsJsonAsync("/api/library", new AddToLibraryRequest
        {
            BookId = book!.Id
        });
        addLibResponse.EnsureSuccessStatusCode();
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();

        // User2, User1'in kitabına başlangıç tarihi eklemeye çalışır -> 404
        var putResponse = await clientUser2.PutAsJsonAsync($"/api/library/{userBook!.Id}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 1 }
        });

        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
    }

    [Fact]
    public async Task PutStartedDate_NotStartedStatus_Returns400BadRequest()
    {
        var (client, _, _) = await CreateUserAsync("user_notstarted@test.com", "Password123!");

        var createBookResponse = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "NotStarted Book",
            Author = "Author",
            PageCount = 150
        });
        createBookResponse.EnsureSuccessStatusCode();
        var book = await createBookResponse.Content.ReadFromJsonAsync<BookResponse>();

        var addLibResponse = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        addLibResponse.EnsureSuccessStatusCode();
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();

        // Kitap NotStarted iken started-date gönderilirse 400 döner
        var putResponse = await client.PutAsJsonAsync($"/api/library/{userBook!.Id}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 1 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
        var err = await putResponse.Content.ReadAsStringAsync();
        Assert.Contains("Yalnızca 'Okunuyor', 'Okundu' veya 'Yarım bırakıldı'", err);
    }

    [Fact]
    public async Task PutStartedDate_ReadingStatus_ValidDate_Returns200Ok()
    {
        var (client, _, _) = await CreateUserAsync("user_reading@test.com", "Password123!");

        var createBookResponse = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Reading Book",
            Author = "Author",
            PageCount = 150
        });
        createBookResponse.EnsureSuccessStatusCode();
        var book = await createBookResponse.Content.ReadFromJsonAsync<BookResponse>();

        var addLibResponse = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        addLibResponse.EnsureSuccessStatusCode();
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();

        // Durumu Reading yapalım (sayfa ilerleterek)
        var progResponse = await client.PutAsJsonAsync($"/api/library/{userBook!.Id}/progress", new UpdateProgressRequest { CurrentPage = 20 });
        progResponse.EnsureSuccessStatusCode();

        // Başlangıç tarihini düzenleyelim
        var putResponse = await client.PutAsJsonAsync($"/api/library/{userBook.Id}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 15 }
        });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2024, 3, 15), updated.StartedDate);
    }

    [Fact]
    public async Task PutStartedDate_EmptyBody_Returns400BadRequest()
    {
        var (client, _, _) = await CreateUserAsync("user_emptybody@test.com", "Password123!");

        // {} boş JSON gövdesi
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var putResponse = await client.PutAsync("/api/library/1/started-date", content);

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }

    [Fact]
    public async Task PutStartedDate_NullStartedDate_Returns400BadRequest()
    {
        var (client, _, _) = await CreateUserAsync("user_nulldate@test.com", "Password123!");

        // {"startedDate": null} gövdesi
        var content = new StringContent("{\"startedDate\": null}", System.Text.Encoding.UTF8, "application/json");
        var putResponse = await client.PutAsync("/api/library/1/started-date", content);

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }
}
