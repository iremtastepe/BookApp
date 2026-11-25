using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.External;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using BookApp.Infrastructure.Repositories;
using BookApp.Tests.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookApp.Tests;

public class BookCreatorTrackingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BookCreatorTrackingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Unit Tests (BookService direct)

    // 1. Manuel kitapta dogru CreatedByUserId, CreatedAt (UTC) ve Manual kaynagi atanmali.
    [Fact]
    public async Task CreateAsync_SetsCreatorId_UtcCreatedAt_AndManualSource()
    {
        var repo = new FakeCreatorBookRepository();
        var lookup = new FakeCreatorBookLookupService(null);
        var service = new BookService(repo, lookup);

        const int expectedUserId = 101;
        var request = new CreateBookRequest
        {
            Title = "Yapay Zeka ve Gelecek",
            Author = "Ali Yılmaz",
            PageCount = 320
        };

        var beforeCall = DateTime.UtcNow;
        var response = await service.CreateAsync(expectedUserId, request);
        var afterCall = DateTime.UtcNow;

        Assert.Single(repo.Books);
        var savedBook = repo.Books[0];

        Assert.Equal(expectedUserId, savedBook.CreatedByUserId);
        Assert.NotNull(savedBook.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, savedBook.CreatedAt.Value.Kind);
        Assert.InRange(savedBook.CreatedAt.Value, beforeCall.AddSeconds(-1), afterCall.AddSeconds(1));
        Assert.Equal(BookCreationSource.Manual, savedBook.CreatedSource);
    }

    // 2. ISBN aramasiyla dis kaynaktan olusturulan kitapta ExternalLookup kaynagi, UTC olusturma ve dogru kullanici olmali.
    [Fact]
    public async Task FindOrFetchByIsbnAsync_WhenCreatedFromExternal_SetsCreatorId_UtcCreatedAt_AndExternalLookupSource()
    {
        const string isbn = "9780140449136";
        const int expectedUserId = 202;

        var externalBook = new ExternalBookDto
        {
            Title = "Suç ve Ceza",
            Author = "Fyodor Dostoyevski",
            Isbn = isbn,
            PageCount = 680,
            PublishedYear = 2003
        };

        var repo = new FakeCreatorBookRepository();
        var lookup = new FakeCreatorBookLookupService(externalBook);
        var service = new BookService(repo, lookup);

        var beforeCall = DateTime.UtcNow;
        var response = await service.FindOrFetchByIsbnAsync(expectedUserId, isbn);
        var afterCall = DateTime.UtcNow;

        Assert.Single(repo.Books);
        var savedBook = repo.Books[0];

        Assert.Equal(expectedUserId, savedBook.CreatedByUserId);
        Assert.NotNull(savedBook.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, savedBook.CreatedAt.Value.Kind);
        Assert.InRange(savedBook.CreatedAt.Value, beforeCall.AddSeconds(-1), afterCall.AddSeconds(1));
        Assert.Equal(BookCreationSource.ExternalLookup, savedBook.CreatedSource);
    }

    // 3. Katalogda mevcut ISBN tekrar sorgulandiginda olusturucu bilgileri degismemeli.
    [Fact]
    public async Task FindOrFetchByIsbnAsync_WhenBookAlreadyExists_PreservesOriginalCreatorFields()
    {
        const string isbn = "9780140449136";
        var originalCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        const int originalUserId = 55;
        const BookCreationSource originalSource = BookCreationSource.Manual;

        var existingBook = new Book
        {
            Id = 1,
            Title = "Orijinal Kitap",
            Author = "Orijinal Yazar",
            Isbn = isbn,
            CreatedByUserId = originalUserId,
            CreatedAt = originalCreatedAt,
            CreatedSource = originalSource
        };

        var repo = new FakeCreatorBookRepository();
        repo.Books.Add(existingBook);

        var lookup = new FakeCreatorBookLookupService(null);
        var service = new BookService(repo, lookup);

        const int differentUserId = 999;
        var result = await service.FindOrFetchByIsbnAsync(differentUserId, isbn);

        // Kitap degismemeli, olusturucu alanlari korunmali
        Assert.Single(repo.Books);
        Assert.Equal(originalUserId, repo.Books[0].CreatedByUserId);
        Assert.Equal(originalCreatedAt, repo.Books[0].CreatedAt);
        Assert.Equal(originalSource, repo.Books[0].CreatedSource);
    }

    // 5. Admin kitap guncellemesinde (UpdateAsync) olusturucu alanlari aynen korunmali, degistirilmemeli.
    [Fact]
    public async Task UpdateAsync_PreservesCreatorFields()
    {
        var originalCreatedAt = new DateTime(2025, 2, 15, 12, 30, 0, DateTimeKind.Utc);
        const int originalUserId = 77;
        const BookCreationSource originalSource = BookCreationSource.ExternalLookup;

        var existingBook = new Book
        {
            Id = 10,
            Title = "Eski Başlık",
            Author = "Eski Yazar",
            CreatedByUserId = originalUserId,
            CreatedAt = originalCreatedAt,
            CreatedSource = originalSource
        };

        var repo = new FakeCreatorBookRepository();
        repo.Books.Add(existingBook);

        var lookup = new FakeCreatorBookLookupService(null);
        var service = new BookService(repo, lookup);

        var updateRequest = new UpdateBookRequest
        {
            Title = "Yeni Güncel Başlık",
            Author = "Yeni Yazar",
            PageCount = 500
        };

        var result = await service.UpdateAsync(10, updateRequest);

        Assert.NotNull(result);
        Assert.Equal("Yeni Güncel Başlık", result.Title);

        var saved = repo.Books[0];
        Assert.Equal(originalUserId, saved.CreatedByUserId);
        Assert.Equal(originalCreatedAt, saved.CreatedAt);
        Assert.Equal(originalSource, saved.CreatedSource);
    }

    // 7. Eski olusturucu alanlari null olan kitaplarla tam uyumluluk (Update veya Get sirasinda hata vermemeli).
    [Fact]
    public async Task LegacyBooks_WithNullCreatorFields_AreHandledSafely()
    {
        var legacyBook = new Book
        {
            Id = 15,
            Title = "Eski Katalog Kitabı",
            Author = "Eski Yazar",
            CreatedByUserId = null,
            CreatedAt = null,
            CreatedSource = null
        };

        var repo = new FakeCreatorBookRepository();
        repo.Books.Add(legacyBook);

        var lookup = new FakeCreatorBookLookupService(null);
        var service = new BookService(repo, lookup);

        // GetById
        var bookDto = await service.GetByIdAsync(15);
        Assert.NotNull(bookDto);
        Assert.Equal("Eski Katalog Kitabı", bookDto.Title);

        // Update
        var updateResult = await service.UpdateAsync(15, new UpdateBookRequest
        {
            Title = "Güncellenmiş Eski Kitap",
            Author = "Eski Yazar"
        });

        Assert.NotNull(updateResult);
        Assert.Null(legacyBook.CreatedByUserId);
        Assert.Null(legacyBook.CreatedAt);
        Assert.Null(legacyBook.CreatedSource);
    }

    // Gecersiz/eksik userId ile olusturma yapilamaz.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_InvalidUserId_ThrowsArgumentException(int invalidUserId)
    {
        var repo = new FakeCreatorBookRepository();
        var lookup = new FakeCreatorBookLookupService(null);
        var service = new BookService(repo, lookup);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(invalidUserId, new CreateBookRequest
            {
                Title = "Test",
                Author = "Test"
            }));
    }

    #endregion

    #region WebApplicationFactory Integration Tests (HTTP & Claims)

    // 4. Kullanicinin request body ile baska bir kullanici adina veya sahte olusturucu bilgisiyle kitap olusturamamasi.
    // JWT claim'indeki kullanici ID'si zorunlu olarak alinmali.
    [Fact]
    public async Task CreateEndpoint_IgnoresExtraBodyFields_UsesJwtUserId()
    {
        var (client, userId) = await CreateAuthorizedClientAsync();

        // Extra creator alanlari iceren JSON gonderelim (sistem bunlari DTO'da tanimlamadigi icin yoksaymali,
        // asil onemlisi veritabanina JWT'deki userId kaydedilmeli).
        var jsonPayload = @"{
            ""title"": ""Müfettiş"",
            ""author"": ""Gogol"",
            ""createdByUserId"": 9999,
            ""createdAt"": ""2020-01-01T00:00:00Z"",
            ""createdSource"": ""ExternalLookup""
        }";

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/books", content);

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(created);

        // DbContext uzerinden kaydedilen kitabin CreatedByUserId'sini dogrula
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bookInDb = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == created.Id);

        Assert.NotNull(bookInDb);
        Assert.Equal(userId, bookInDb.CreatedByUserId);
        Assert.NotEqual(9999, bookInDb.CreatedByUserId);
        Assert.Equal(BookCreationSource.Manual, bookInDb.CreatedSource);
        Assert.NotNull(bookInDb.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, bookInDb.CreatedAt.Value.Kind);
    }

    private async Task<(HttpClient Client, int UserId)> CreateAuthorizedClientAsync()
    {
        var username = $"user_{Guid.NewGuid():N}";
        var email = $"{username}@test.com";
        const string password = "Password123!";
        int createdUserId;

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User
            {
                UserName = username,
                Email = email
            };

            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded);
            createdUserId = user.Id;
        }

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        return (client, createdUserId);
    }

    #endregion

    #region PostgreSQL Integration Test (Kullanici silinince kitabin korunmasi ve ON DELETE SET NULL)

    // 9. Gerçek PostgreSQL test veritabanında kullanıcı silinince kitabın korunması ve FK'nin null olması.
    [PostgresFact]
    public async Task Postgres_WhenUserDeleted_BookIsPreserved_AndCreatedByUserIdSetToNull()
    {
        var connectionString = PostgresTestConnectionGuard.Validate(
            PostgresTestSettings.RawConnectionString);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);

        // 1. Yeni bir test kullanicisi olustur
        var user = new User
        {
            UserName = $"pg_test_user_{Guid.NewGuid():N}",
            Email = $"pg_test_{Guid.NewGuid():N}@test.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // 2. Bu kullanici tarafindan olusturulmus bir kitap ekle
        var book = new Book
        {
            Title = $"Postgres Test Kitabı {Guid.NewGuid():N}",
            Author = "Test Yazar",
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedSource = BookCreationSource.Manual
        };
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var bookId = book.Id;
        var userId = user.Id;

        // EF Core takibini temizle.
        // Boylece ON DELETE SET NULL islemini PostgreSQL gerceklestirir.
        context.ChangeTracker.Clear();

        // 3. Kullaniciyi sil
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        // 4. Kitabi ayri bir sorgu ile getir ve durumunu incele
        var bookAfterUserDelete = await context.Books
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookId);

        Assert.NotNull(bookAfterUserDelete); // Kitap silinmedi
        Assert.Null(bookAfterUserDelete.CreatedByUserId); // FK null oldu (ON DELETE SET NULL)

        // Temizlik (Postgres test veritabani test izolasyonu)
        context.Books.Remove(new Book { Id = bookId });
        await context.SaveChangesAsync();
    }

    #endregion

    #region Fake Classes for Unit Tests

    private sealed class FakeCreatorBookRepository : IBookRepository
    {
        public List<Book> Books { get; } = new();

        public Task<Book?> GetByIdAsync(int id)
        {
            return Task.FromResult(Books.FirstOrDefault(b => b.Id == id));
        }

        public Task<Book?> GetByIsbnAsync(string isbn)
        {
            return Task.FromResult(Books.FirstOrDefault(b => b.Isbn == isbn));
        }

        public Task<(List<Book> Items, int TotalCount)> GetPagedAsync(string? search, int page, int pageSize)
        {
            return Task.FromResult((Books, Books.Count));
        }

        public Task<bool> IsInAnyLibraryAsync(int bookId)
        {
            return Task.FromResult(false);
        }

        public Task AddAsync(Book book)
        {
            book.Id = Books.Count == 0 ? 1 : Books.Max(b => b.Id) + 1;
            Books.Add(book);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Book book)
        {
            return Task.CompletedTask;
        }

        public Task<int?> GetMaxCurrentPageAsync(int bookId)
        {
            return Task.FromResult<int?>(null);
        }

        public Task<int?> GetMaxNonReadCurrentPageAsync(int bookId)
        {
            return Task.FromResult<int?>(null);
        }

        public Task<List<UserBook>> GetReadUserBooksByBookIdAsync(int bookId)
        {
            return Task.FromResult(new List<UserBook>());
        }

        public Task DeleteAsync(Book book)
        {
            Books.Remove(book);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCreatorBookLookupService : IBookLookupService
    {
        private readonly ExternalBookDto? _dto;

        public FakeCreatorBookLookupService(ExternalBookDto? dto)
        {
            _dto = dto;
        }

        public Task<ExternalBookDto?> SearchByIsbnAsync(string isbn)
        {
            return Task.FromResult(_dto);
        }
    }

    #endregion
}