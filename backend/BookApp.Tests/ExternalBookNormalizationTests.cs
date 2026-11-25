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
        string? value)
    {
        var result = await ImportAsync(book =>
        {
            book.Title = value!;
            book.Author = value!;
        });

        Assert.Equal("Bilinmeyen Kitap", result.Title);
        Assert.Equal("Bilinmeyen Yazar", result.Author);
    }

    // Uzun baslik ve yazar sinirlandirilir.
    [Fact]
    public async Task LongTitleAndAuthor_AreTruncated()
    {
        var result = await ImportAsync(book =>
        {
            book.Title = " " + new string('T', 305) + " ";
            book.Author = " " + new string('A', 205) + " ";
        });

        Assert.Equal(300, result.Title.Length);
        Assert.Equal(200, result.Author.Length);

        Assert.Equal(new string('T', 300), result.Title);
        Assert.Equal(new string('A', 200), result.Author);
    }

    // Normal degerler korunur, gereksiz bosluklar temizlenir.
    [Fact]
    public async Task ValidFields_ArePreservedAndTrimmed()
    {
        var result = await ImportAsync(book =>
        {
            book.Title = "  Dördüncü Kanat  ";
            book.Author = "  Rebecca Yarros  ";
            book.Genre = "  Fantastik  ";
            book.PageCount = 456;
            book.PublishedYear = 2023;
        });

        Assert.Equal("Dördüncü Kanat", result.Title);
        Assert.Equal("Rebecca Yarros", result.Author);
        Assert.Equal("Fantastik", result.Genre);
        Assert.Equal(456, result.PageCount);
        Assert.Equal(2023, result.PublishedYear);
    }

    // Gecersiz sayfa sayilari null olur.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10001)]
    public async Task InvalidPageCount_BecomesNull(
        int pageCount)
    {
        var result = await ImportAsync(book =>
        {
            book.PageCount = pageCount;
        });

        Assert.Null(result.PageCount);
    }

    // Sinir degerlerindeki sayfa sayilari kabul edilir.
    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task ValidPageCount_IsPreserved(
        int pageCount)
    {
        var result = await ImportAsync(book =>
        {
            book.PageCount = pageCount;
        });

        Assert.Equal(pageCount, result.PageCount);
    }

    // Gecersiz yayin yillari null olur.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2101)]
    public async Task InvalidPublishedYear_BecomesNull(
        int year)
    {
        var result = await ImportAsync(book =>
        {
            book.PublishedYear = year;
        });

        Assert.Null(result.PublishedYear);
    }

    // Sinir degerlerindeki yayin yillari kabul edilir.
    [Theory]
    [InlineData(1)]
    [InlineData(2100)]
    public async Task ValidPublishedYear_IsPreserved(
        int year)
    {
        var result = await ImportAsync(book =>
        {
            book.PublishedYear = year;
        });

        Assert.Equal(year, result.PublishedYear);
    }

    // Bos tur bilgisi null olur.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyGenre_BecomesNull(
        string? genre)
    {
        var result = await ImportAsync(book =>
        {
            book.Genre = genre;
        });

        Assert.Null(result.Genre);
    }

    // Uzun tur bilgisi 100 karaktere indirilir.
    [Fact]
    public async Task LongGenre_IsTruncated()
    {
        var result = await ImportAsync(book =>
        {
            book.Genre = new string('G', 150);
        });

        Assert.Equal(100, result.Genre?.Length);
        Assert.Equal(new string('G', 100), result.Genre);
    }

    private sealed class FakeBookLookupService : IBookLookupService
    {
        private readonly ExternalBookDto _book;

        public FakeBookLookupService(ExternalBookDto book)
        {
            _book = book;
        }

        public Task<ExternalBookDto?> SearchByIsbnAsync(
            string isbn)
        {
            return Task.FromResult<ExternalBookDto?>(_book);
        }
    }

    private sealed class FakeBookRepository : IBookRepository
    {
        public List<Book> Books { get; } = new();

        public Task<Book?> GetByIdAsync(int id)
        {
            return Task.FromResult(
                Books.FirstOrDefault(book => book.Id == id));
        }

        public Task<Book?> GetByIsbnAsync(string isbn)
        {
            return Task.FromResult(
                Books.FirstOrDefault(book => book.Isbn == isbn));
        }

        public Task<(List<Book> Items, int TotalCount)> GetPagedAsync(
            string? search,
            int page,
            int pageSize)
        {
            return Task.FromResult(
                (Items: Books.ToList(), TotalCount: Books.Count));
        }

        public Task<bool> IsInAnyLibraryAsync(int bookId)
        {
            return Task.FromResult(false);
        }

        public Task AddAsync(Book book)
        {
            book.Id = Books.Count + 1;
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
}
