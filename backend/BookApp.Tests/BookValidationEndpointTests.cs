using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookApp.Tests;

public class BookValidationEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BookValidationEndpointTests(
        CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync(
        bool admin = false)
    {
        var username = $"validation_{Guid.NewGuid():N}";
        var email = $"{username}@test.com";
        const string password = "Password123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<User>>();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole<int>>>();

            var user = new User
            {
                UserName = username,
                Email = email
            };

            var result = await userManager.CreateAsync(
                user, password);

            Assert.True(
                result.Succeeded,
                string.Join(", ",
                    result.Errors.Select(e => e.Description)));

            if (admin)
            {
                if (!await roleManager.RoleExistsAsync("Admin"))
                {
                    var roleResult = await roleManager.CreateAsync(
                        new IdentityRole<int>("Admin"));

                    Assert.True(roleResult.Succeeded);
                }

                var assignment = await userManager.AddToRoleAsync(
                    user, "Admin");

                Assert.True(assignment.Succeeded);
            }
        }

        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                Email = email,
                Password = password
            });

        loginResponse.EnsureSuccessStatusCode();

        var auth = await loginResponse.Content
            .ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", auth.Token);

        return client;
    }

    private static CreateBookRequest ValidCreateRequest(
        string? coverUrl = "https://example.com/cover.jpg",
        string? description = null)
    {
        return new CreateBookRequest
        {
            Title = "API Test Kitabı",
            Author = "Test Yazarı",
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = 250
        };
    }

    private static UpdateBookRequest ValidUpdateRequest(
        string? coverUrl,
        string? description = null)
    {
        return new UpdateBookRequest
        {
            Title = "Güncellenmiş API Test Kitabı",
            Author = "Test Yazarı",
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = 250
        };
    }

    // Geçersiz kapak URL'si API'den 400 döndürmeli.
    [Theory]
    [InlineData("string")]
    [InlineData("ftp://example.com/cover.jpg")]
    [InlineData("invalid-url")]
    public async Task Post_InvalidCoverUrl_Returns400(
        string url)
    {
        using var client = await CreateAuthorizedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/books",
            ValidCreateRequest(coverUrl: url));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    // Çok uzun manuel açıklama API'den 400 döndürmeli.
    [Fact]
    public async Task Post_DescriptionOver2000_Returns400()
    {
        using var client = await CreateAuthorizedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/books",
            ValidCreateRequest(
                description: new string('a', 2001)));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    // Geçerli URL ve 2000 karakter açıklama kabul edilmeli.
    [Fact]
    public async Task Post_ValidData_Returns201()
    {
        using var client = await CreateAuthorizedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/books",
            ValidCreateRequest(
                description: new string('a', 2000)));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var book = await response.Content
            .ReadFromJsonAsync<BookResponse>();

        Assert.NotNull(book);
        Assert.Equal(2000, book.Description?.Length);
        Assert.Equal(
            "https://example.com/cover.jpg",
            book.CoverImageUrl);
    }

    // Admin geçersiz yeni URL ile kitap güncelleyememeli.
    [Fact]
    public async Task Put_InvalidNewCoverUrl_Returns400()
    {
        using var client = await CreateAuthorizedClientAsync(
            admin: true);

        var createResponse = await client.PostAsJsonAsync(
            "/api/books",
            ValidCreateRequest());

        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content
            .ReadFromJsonAsync<BookResponse>();

        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/books/{created.Id}",
            ValidUpdateRequest("invalid-new-url"));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            updateResponse.StatusCode);

        var getResponse = await client.GetAsync(
            $"/api/books/{created.Id}");

        var unchanged = await getResponse.Content
            .ReadFromJsonAsync<BookResponse>();

        Assert.NotNull(unchanged);
        Assert.Equal(
            "https://example.com/cover.jpg",
            unchanged.CoverImageUrl);
        Assert.Equal(
            "API Test Kitabı",
            unchanged.Title);
    }

    // Eski "string" URL aynen gönderilirse güncelleme yapılabilmeli.
    [Fact]
    public async Task Put_UnchangedLegacyCover_Returns200()
    {
        using var client = await CreateAuthorizedClientAsync(
            admin: true);

        int bookId;

        // Yalnızca testin InMemory veritabanına eski kayıt ekle.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var book = new Book
            {
                Title = "Eski Test Kitabı",
                Author = "Test Yazarı",
                CoverImageUrl = "string",
                PageCount = 250
            };

            db.Books.Add(book);
            await db.SaveChangesAsync();

            bookId = book.Id;
        }

        var response = await client.PutAsJsonAsync(
            $"/api/books/{bookId}",
            ValidUpdateRequest(
                coverUrl: "string",
                description: "Güncellenmiş açıklama"));

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var updated = await response.Content
            .ReadFromJsonAsync<BookResponse>();

        Assert.NotNull(updated);
        Assert.Equal("string", updated.CoverImageUrl);
        Assert.Equal(
            "Güncellenmiş API Test Kitabı",
            updated.Title);
        Assert.Equal(
            "Güncellenmiş açıklama",
            updated.Description);
    }
}
