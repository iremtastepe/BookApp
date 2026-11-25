using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.External;
using BookApp.Application.Interfaces;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Application.Services;
using BookApp.Domain.Entities;

namespace BookApp.Tests;

public class ExternalBookNormalizationTests
{
    private const string ValidIsbn = "9780306406157";
    private const int TestUserId = 42;

    private static async Task<BookResponse> ImportAsync(
        Action<ExternalBookDto> configure)
    {
        var externalBook = new ExternalBookDto
        {
            Title = "Test Kitabı",
            Author = "Test Yazarı",
            Isbn = ValidIsbn,
            PageCount = 250,
            PublishedYear = 2020,
            Genre = "Roman"
        };

        configure(externalBook);

        var repository = new FakeBookRepository();
        var lookupService = new FakeBookLookupService(externalBook);

        var service = new BookService(
            repository,
            lookupService);

        var result = await service.FindOrFetchByIsbnAsync(
            TestUserId,
            ValidIsbn);

        Assert.Single(repository.Books);
        Assert.Equal(ValidIsbn, result.Isbn);

        return result;
    }

    // Bos veya null baslik ve yazar varsayilan deger alir.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyTitleAndAuthor_UseFallback(
        string? emptyValue)
    {
        var result = await ImportAsync(book =>
        {
            book.Title = emptyValue!;
            book.Author = emptyValue!;
        });

        Assert.Equal("Bilinmeyen Kitap", result.Title);
        Assert.Equal("Bilinmeyen Yazar", result.Author);
    }

    // 300 karakteri asan baslik kirpilir.
    [Fact]
    public async Task TitleOver300Characters_IsTruncated()
    {
        var longTitle = new string('a', 305);

        var result = await ImportAsync(book =>
        {
            book.Title = longTitle;
        });

        Assert.Equal(300, result.Title.Length);
        Assert.Equal(new string('a', 300), result.Title);
    }

    // 200 karakteri asan yazar adi kirpilir.
    [Fact]
    public async Task AuthorOver200Characters_IsTruncated()
    {
        var longAuthor = new string('b', 205);

        var result = await ImportAsync(book =>
        {
            book.Author = longAuthor;
        });

        Assert.Equal(200, result.Author.Length);
        Assert.Equal(new string('b', 200), result.Author);
    }

    // 100 karakteri asan tur kirpilir.
    [Fact]
    public async Task GenreOver100Characters_IsTruncated()
    {
        var longGenre = new string('g', 105);

        var result = await ImportAsync(book =>
        {
            book.Genre = longGenre;
        });

        Assert.Equal(100, result.Genre?.Length);
        Assert.Equal(new string('g', 100), result.Genre);
    }

    // Bos tur null yapilir.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyGenre_BecomesNull(string? emptyGenre)
    {
        var result = await ImportAsync(book =>
        {
            book.Genre = emptyGenre;
        });

        Assert.Null(result.Genre);
    }

    // Gecersiz sayfa sayisi null yapilir.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public async Task InvalidPageCount_BecomesNull(int invalidPages)
    {
        var result = await ImportAsync(book =>
        {
            book.PageCount = invalidPages;
        });

        Assert.Null(result.PageCount);
    }

    // Gecerli sinirdaki sayfa sayilari korunur.
    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task BoundaryPageCount_IsPreserved(int validPages)
    {
        var result = await ImportAsync(book =>
        {
            book.PageCount = validPages;
        });

        Assert.Equal(validPages, result.PageCount);
    }

    // Gecersiz yayin yili null yapilir.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2101)]
    public async Task InvalidPublishedYear_BecomesNull(int invalidYear)
    {
        var result = await ImportAsync(book =>
        {
            book.PublishedYear = invalidYear;
        });

        Assert.Null(result.PublishedYear);
    }

    // Gecerli sinirdaki yayin yillari korunur.
    [Theory]
    [InlineData(1)]
    [InlineData(2100)]
    public async Task BoundaryPublishedYear_IsPreserved(int validYear)
    {
        var result = await ImportAsync(book =>
        {
            book.PublishedYear = validYear;
        });

        Assert.Equal(validYear, result.PublishedYear);
    }

    // Gecersiz kapak adresi null yapilir.
    [Theory]
    [InlineData("ftp://example.com/cover.jpg")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public async Task InvalidCoverUrl_BecomesNull(string invalidUrl)
    {
        var result = await ImportAsync(book =>
        {
            book.CoverImageUrl = invalidUrl;
        });

        Assert.Null(result.CoverImageUrl);
    }

    // 500 karakteri asan kapak adresi null yapilir.
    [Fact]
    public async Task CoverUrlOver500Characters_BecomesNull()
    {
        var longUrl = "https://example.com/" + new string('a', 500);

        var result = await ImportAsync(book =>
        {
            book.CoverImageUrl = longUrl;
        });

        Assert.Null(result.CoverImageUrl);
    }

    // 2000 karakteri asan aciklama kirpilir.
    [Fact]
    public async Task DescriptionOver2000Characters_IsTruncated()
    {
        var longDescription = new string('d', 2050);

        var result = await ImportAsync(book =>
        {
            book.Description = longDescription;
        });

        Assert.Equal(2000, result.Description?.Length);
        Assert.Equal(new string('d', 2000), result.Description);
    }

    // Testler icin bellek ici sahte repository.
    private sealed class FakeBookRepository : IBookRepository
    {
        public List<Book> Books { get; } = new();

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
            book.Id = Books.Count == 0
                ? 1
                : Books.Max(b => b.Id) + 1;

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
