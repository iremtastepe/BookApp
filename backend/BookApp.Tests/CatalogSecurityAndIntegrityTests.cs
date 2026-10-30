using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookApp.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _dbRoot = new();
    private static readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _internalProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // AppDbContext ve DbContextOptions servis kayıtlarını tamamen temizle
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                            d.ServiceType == typeof(DbContextOptions) ||
                            d.ServiceType == typeof(AppDbContext))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            // Test için izole InMemory veritabanı ekle.
            // Her factory (yani her test sınıfı) KENDİ adlı veritabanını kullanır (_dbName = Guid).
            // Sabit bir ad ("SharedTestDb") kullanılırsa iki test sınıfı paralel çalışırken iki sunucu
            // aynı veritabanına bağlanır, ikisinin seeder'ı da "Admin" rolünü oluşturur ve rol çift kayıt olur.
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName, _dbRoot);
                options.UseInternalServiceProvider(_internalProvider);
            });
        });
    }
}

public class CatalogSecurityAndIntegrityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CatalogSecurityAndIntegrityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, User User, string Token)> CreateUserWithRoleAsync(string email, string password, string? role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

        if (role != null && !await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<int>(role));
        }

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

        if (role != null && !await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
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
    public async Task Scenario1_NoToken_PutAndDelete_Returns401Unauthorized()
    {
        var client = _factory.CreateClient();

        var putResponse = await client.PutAsJsonAsync("/api/books/999", new UpdateBookRequest
        {
            Title = "Unauthorized Update",
            Author = "Author"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync("/api/books/999");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Scenario2_NormalUser_PutAndDelete_Returns403Forbidden()
    {
        var (client, _, _) = await CreateUserWithRoleAsync("normal_user1@test.com", "Password123!", null);

        var putResponse = await client.PutAsJsonAsync("/api/books/1", new UpdateBookRequest
        {
            Title = "Forbidden Update",
            Author = "Author"
        });
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync("/api/books/1");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Scenario3_Admin_ValidPut_Returns200Ok()
    {
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_creator@test.com", "Password123!", null);
        var createResponse = await normalClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Original Title",
            Author = "Original Author",
            PageCount = 300
        });
        createResponse.EnsureSuccessStatusCode();
        var createdBook = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_user1@test.com", "AdminPass123!", "Admin");
        var putResponse = await adminClient.PutAsJsonAsync($"/api/books/{createdBook!.Id}", new UpdateBookRequest
        {
            Title = "Updated Title by Admin",
            Author = "Original Author",
            PageCount = 320
        });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal("Updated Title by Admin", updated!.Title);
        Assert.Equal(320, updated.PageCount);
    }

    [Fact]
    public async Task Scenario4_Admin_PutWithInvalidPageCountBelowCurrentPage_Returns409Conflict()
    {
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_reader@test.com", "Password123!", null);
        var createResponse = await normalClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Integrity Book",
            Author = "Author",
            PageCount = 400
        });
        createResponse.EnsureSuccessStatusCode();
        var createdBook = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        // Kitabı kütüphaneye ekle ve sayfayı 350 yap
        var addLibResponse = await normalClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest
        {
            BookId = createdBook!.Id
        });
        addLibResponse.EnsureSuccessStatusCode();
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();

        var updateProgressResponse = await normalClient.PutAsJsonAsync($"/api/library/{userBook!.Id}/progress", new UpdateProgressRequest
        {
            CurrentPage = 350
        });
        updateProgressResponse.EnsureSuccessStatusCode();

        // Admin sayfa sayısını 250'ye düşürmeye çalışır -> 409 dönmeli
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_user2@test.com", "AdminPass123!", "Admin");
        var putResponse = await adminClient.PutAsJsonAsync($"/api/books/{createdBook.Id}", new UpdateBookRequest
        {
            Title = "Integrity Book",
            Author = "Author",
            PageCount = 250
        });

        Assert.Equal(HttpStatusCode.Conflict, putResponse.StatusCode);
        var errorContent = await putResponse.Content.ReadAsStringAsync();
        Assert.Contains("350", errorContent);
    }

    [Fact]
    public async Task Scenario5_Admin_DeleteUnusedBook_Returns204NoContent()
    {
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_creator2@test.com", "Password123!", null);
        var createResponse = await normalClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Unused Book",
            Author = "Author"
        });
        createResponse.EnsureSuccessStatusCode();
        var createdBook = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_user3@test.com", "AdminPass123!", "Admin");
        var deleteResponse = await adminClient.DeleteAsync($"/api/books/{createdBook!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // GetById 404 dönmeli
        var getResponse = await normalClient.GetAsync($"/api/books/{createdBook.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Scenario6And7_Admin_DeleteBookInLibrary_Returns409AndPreservesData()
    {
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_keeper@test.com", "Password123!", null);
        var createResponse = await normalClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Protected Library Book",
            Author = "Author"
        });
        createResponse.EnsureSuccessStatusCode();
        var createdBook = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        var addLibResponse = await normalClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest
        {
            BookId = createdBook!.Id
        });
        addLibResponse.EnsureSuccessStatusCode();
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();

        // Puan ve yorum ekle
        // Önce durumu Read yap (puan/yorum Read veya DidNotFinish durumunda verilebilir)
        var statusResponse = await normalClient.PutAsJsonAsync($"/api/library/{userBook!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = BookApp.Domain.Enums.ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        statusResponse.EnsureSuccessStatusCode();

        var reviewResponse = await normalClient.PutAsJsonAsync($"/api/library/{userBook.Id}/review", new UpdateReviewAndRatingRequest
        {
            Rating = 4.5m,
            Review = "Harika bir kitap"
        });
        reviewResponse.EnsureSuccessStatusCode();

        // Admin silmeye kalkışır -> 409
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_user4@test.com", "AdminPass123!", "Admin");
        var deleteResponse = await adminClient.DeleteAsync($"/api/books/{createdBook.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        // Kullanıcının verileri eksiksiz duruyor mu
        var getLibResponse = await normalClient.GetAsync($"/api/library");
        if (!getLibResponse.IsSuccessStatusCode)
        {
            var err = await getLibResponse.Content.ReadAsStringAsync();
            throw new Exception("GetLibrary failed: " + err);
        }
        Assert.Equal(HttpStatusCode.OK, getLibResponse.StatusCode);
        var pagedResult = await getLibResponse.Content.ReadFromJsonAsync<PagedResponse<UserBookDto>>();
        Assert.NotNull(pagedResult);
        var fetchedUserBook = pagedResult.Items.FirstOrDefault(b => b.BookId == createdBook.Id);
        Assert.NotNull(fetchedUserBook);
        Assert.Equal(4.5m, fetchedUserBook.Rating);
        Assert.Equal("Harika bir kitap", fetchedUserBook.Review);
    }

    [Fact]
    public async Task Scenario8_NormalUser_CanCreateManualBookWithoutIsbnAndAddToLibrary()
    {
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_manual@test.com", "Password123!", null);

        var createResponse = await normalClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Manuel Özel Kitap",
            Author = "Yerel Yazar",
            Isbn = null, // ISBN yok
            PageCount = 180,
            Genre = "Anı",
            PublishedYear = 2021
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var book = await createResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(book);
        Assert.Equal("Manuel Özel Kitap", book.Title);
        Assert.Equal(180, book.PageCount);
        Assert.Null(book.Isbn);

        // Kendi kitaplığına ekleyebilmeli
        var addLibResponse = await normalClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest
        {
            BookId = book.Id
        });
        Assert.Equal(HttpStatusCode.Created, addLibResponse.StatusCode);
        var userBook = await addLibResponse.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.NotNull(userBook);
        Assert.Equal(180, userBook.PageCount);
    }

    [Fact]
    public async Task Scenario9_NormalUser_RegistrationDoesNotGrantAdminRole()
    {
        var client = _factory.CreateClient();
        var regEmail = "fresh_user@test.com";
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "freshuser",
            Email = regEmail,
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        });
        regResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(regEmail);
        Assert.NotNull(user);
        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        Assert.False(isAdmin);
    }
}