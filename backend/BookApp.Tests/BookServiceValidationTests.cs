using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.External;
using BookApp.Application.Interfaces;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Application.Services;
using BookApp.Domain.Entities;

namespace BookApp.Tests;

public class BookServiceValidationTests
{
    private const string ValidIsbn = "9780306406157";

    private static (BookService Service, FakeBookRepository Repo)
        CreateService(ExternalBookDto? externalBook = null)
    {
        var repo = new FakeBookRepository();
        var lookup = new FakeBookLookupService(externalBook);

        return (new BookService(repo, lookup), repo);
    }

    private static CreateBookRequest NewCreateRequest(
        string? coverUrl = null,
        string? description = null)
    {
        return new CreateBookRequest
        {
            Title = "Test Kitabı",
            Author = "Test Yazarı",
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = 250
        };
    }

    private static UpdateBookRequest NewUpdateRequest(
        string? coverUrl = null,
        string? description = null)
    {
        return new UpdateBookRequest
        {
            Title = "Güncellenmiş Kitap",
            Author = "Test Yazarı",
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = 250
        };
    }

    // 1-2: HTTP ve HTTPS adresleri kabul edilir.
    [Theory]
    [InlineData("https://example.com/cover.jpg")]
    [InlineData("http://example.com/cover.jpg")]
    public async Task Create_ValidCoverUrl_IsAccepted(string url)
    {
        var (service, repo) = CreateService();

        var result = await service.CreateAsync(
            NewCreateRequest(coverUrl: url));

        Assert.Equal(url, result.CoverImageUrl);
        Assert.Equal(1, repo.AddCalls);
    }

    // 3-5: Gecersiz kapak adresleri reddedilir.
    [Theory]
    [InlineData("string")]
    [InlineData("ftp://example.com/cover.jpg")]
    [InlineData("not-a-valid-url")]
    public async Task Create_InvalidCoverUrl_IsRejected(string url)
    {
        var (service, repo) = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                NewCreateRequest(coverUrl: url)));

        Assert.Equal(0, repo.AddCalls);
        Assert.Empty(repo.Books);
    }

    // 6: Kapak adresi 500 karakteri asamaz.
    [Fact]
    public async Task Create_CoverUrlOver500Characters_IsRejected()
    {
        var (service, repo) = CreateService();

        var url = "https://example.com/" + new string('a', 500);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                NewCreateRequest(coverUrl: url)));

        Assert.Equal(0, repo.AddCalls);
    }

    // 7: Bos kapak adresi null olarak kaydedilir.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyCoverUrl_BecomesNull(string? url)
    {
        var (service, repo) = CreateService();

        var result = await service.CreateAsync(
            NewCreateRequest(coverUrl: url));

        Assert.Null(result.CoverImageUrl);
        Assert.Equal(1, repo.AddCalls);
    }

    // 8: Tam 2000 karakterlik aciklama kabul edilir.
    [Fact]
    public async Task Create_DescriptionExactly2000_IsAccepted()
    {
        var (service, repo) = CreateService();
        var description = new string('a', 2000);

        var result = await service.CreateAsync(
            NewCreateRequest(description: description));

        Assert.Equal(2000, result.Description?.Length);
        Assert.Equal(1, repo.AddCalls);
    }

    // 9: 2000 karakteri asan manuel aciklama reddedilir.
    [Fact]
    public async Task Create_DescriptionOver2000_IsRejected()
    {
        var (service, repo) = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                NewCreateRequest(
                    description: new string('a', 2001))));

        Assert.Equal(0, repo.AddCalls);
    }

    // 10: Eski "string" kapak adresi degistirilmezse korunur.
    [Fact]
    public async Task Update_UnchangedLegacyCoverUrl_IsPreserved()
    {
        var (service, repo) = CreateService();

        repo.Books.Add(new Book
        {
            Id = 11,
            Title = "Dördüncü Kanat",
            Author = "Test Yazarı",
            CoverImageUrl = "string",
            PageCount = 250
        });

        var request = NewUpdateRequest(coverUrl: "string");

        var result = await service.UpdateAsync(11, request);

        Assert.NotNull(result);
        Assert.Equal("string", result.CoverImageUrl);
        Assert.Equal("Güncellenmiş Kitap", result.Title);
        Assert.Equal(1, repo.UpdateCalls);
    }

    // 11: Eski URL yerine yeni gecersiz URL yazilamaz.
    [Fact]
    public async Task Update_NewInvalidCoverUrl_IsRejected()
    {
        var (service, repo) = CreateService();

        repo.Books.Add(new Book
        {
            Id = 11,
            Title = "Eski Başlık",
            Author = "Test Yazarı",
            CoverImageUrl = "string",
            PageCount = 250
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(
                11,
                NewUpdateRequest(coverUrl: "invalid-url")));

        Assert.Equal(0, repo.UpdateCalls);
        Assert.Equal("string", repo.Books[0].CoverImageUrl);
        Assert.Equal("Eski Başlık", repo.Books[0].Title);
    }

    // 12: Eski hatali URL gecerli bir URL ile degistirilebilir.
    [Fact]
    public async Task Update_LegacyCoverUrl_CanBeReplaced()
    {
        var (service, repo) = CreateService();

        repo.Books.Add(new Book
        {
            Id = 11,
            Title = "Test Kitabı",
            Author = "Test Yazarı",
            CoverImageUrl = "string"
        });

        var result = await service.UpdateAsync(
            11,
            NewUpdateRequest(
                coverUrl: "https://example.com/new-cover.jpg"));

        Assert.NotNull(result);
        Assert.Equal(
            "https://example.com/new-cover.jpg",
            result.CoverImageUrl);

        Assert.Equal(1, repo.UpdateCalls);
    }

    // 13: Guncellemede uzun aciklama engellenir.
    [Fact]
    public async Task Update_DescriptionOver2000_IsRejected()
    {
        var (service, repo) = CreateService();

        repo.Books.Add(new Book
        {
            Id = 1,
            Title = "Eski Başlık",
            Author = "Test Yazarı"
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(
                1,
                NewUpdateRequest(
                    description: new string('a', 2001))));

        Assert.Equal(0, repo.UpdateCalls);
        Assert.Equal("Eski Başlık", repo.Books[0].Title);
    }

    // 14: Dis API'den gecersiz URL gelirse kapak null olur.
    [Fact]
    public async Task ExternalBook_InvalidCoverUrl_BecomesNull()
    {
        var external = new ExternalBookDto
        {
            Title = "Dış API Kitabı",
            Author = "Test Yazarı",
            Isbn = ValidIsbn,
            CoverImageUrl = "invalid-cover-url",
            Description = "Kısa açıklama"
        };

        var (service, repo) = CreateService(external);

        var result = await service.FindOrFetchByIsbnAsync(
            ValidIsbn);

        Assert.Null(result.CoverImageUrl);
        Assert.Equal(1, repo.AddCalls);
        Assert.Single(repo.Books);
    }

    // 15: Dis API'nin uzun aciklamasi 2000 karaktere indirilir.
    [Fact]
    public async Task ExternalBook_LongDescription_IsTruncated()
    {
        var external = new ExternalBookDto
        {
            Title = "Dış API Kitabı",
            Author = "Test Yazarı",
            Isbn = ValidIsbn,
            CoverImageUrl = "https://example.com/cover.jpg",
            Description = new string('a', 2500)
        };

        var (service, repo) = CreateService(external);

        var result = await service.FindOrFetchByIsbnAsync(
            ValidIsbn);

        Assert.Equal(2000, result.Description?.Length);
        Assert.Equal(
            "https://example.com/cover.jpg",
            result.CoverImageUrl);

        Assert.Equal(1, repo.AddCalls);
    }

    // Testler icin bellek ici sahte repository.
    private sealed class FakeBookRepository : IBookRepository
    {
        public List<Book> Books { get; } = new();

        public int AddCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<Book?> GetByIdAsync(int id)
        {
            return Task.FromResult(
                Books.FirstOrDefault(b => b.Id == id));
        }

        public Task<Book?> GetByIsbnAsync(string isbn)
        {
            return Task.FromResult(
                Books.FirstOrDefault(b => b.Isbn == isbn));
        }

        public Task<(List<Book> Items, int TotalCount)> GetPagedAsync(
            string? search,
            int page,
            int pageSize)
        {
            var items = Books.ToList();

            return Task.FromResult(
                (Items: items, TotalCount: items.Count));
        }

        public Task<bool> IsInAnyLibraryAsync(int bookId)
        {
            return Task.FromResult(false);
        }

        public Task AddAsync(Book book)
        {
            AddCalls++;

            book.Id = Books.Count == 0
                ? 1
                : Books.Max(b => b.Id) + 1;

            Books.Add(book);

            return Task.CompletedTask;
        }

        public Task UpdateAsync(Book book)
        {
            UpdateCalls++;
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

        public Task<List<UserBook>> GetReadUserBooksByBookIdAsync(
            int bookId)
        {
            return Task.FromResult(new List<UserBook>());
        }

        public Task DeleteAsync(Book book)
        {
            Books.Remove(book);
            return Task.CompletedTask;
        }
    }

    // Testler icin sahte dis kitap servisi.
    private sealed class FakeBookLookupService : IBookLookupService
    {
        private readonly ExternalBookDto? _book;

        public FakeBookLookupService(ExternalBookDto? book)
        {
            _book = book;
        }

        public Task<ExternalBookDto?> SearchByIsbnAsync(string isbn)
        {
            return Task.FromResult(_book);
        }
    }
}
