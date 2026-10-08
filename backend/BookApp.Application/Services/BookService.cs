using BookApp.Application.DTOs.Books;
using BookApp.Application.Helpers;
using BookApp.Application.Interfaces;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Domain.Entities;

namespace BookApp.Application.Services;

public class BookService : IBookService
{
    private readonly IBookRepository _repo;
    private readonly IBookLookupService _lookupService;

    public BookService(IBookRepository repo, IBookLookupService lookupService)
    {
        _repo = repo;
        _lookupService = lookupService;
    }

    public async Task<PagedResponse<BookResponse>> GetBooksAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (items, total) = await _repo.GetPagedAsync(search, page, pageSize);

        return new PagedResponse<BookResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<BookResponse?> GetByIdAsync(int id)
    {
        var book = await _repo.GetByIdAsync(id);
        return book is null ? null : ToResponse(book);
    }

    public async Task<BookResponse> CreateAsync(CreateBookRequest request)
    {
        // DEGISTI: ISBN doluysa temizle, dogrula, ISBN-13'e cevir
        var normalizedIsbn = string.IsNullOrWhiteSpace(request.Isbn)
            ? null
            : IsbnHelper.Normalize(request.Isbn);

        if (normalizedIsbn is not null &&
            await _repo.GetByIsbnAsync(normalizedIsbn) is not null)
        {
            throw new InvalidOperationException("Bu ISBN ile kayıtlı bir kitap zaten var.");
        }

        var book = new Book
        {
            Title = request.Title.Trim(),
            Author = request.Author.Trim(),
            Isbn = normalizedIsbn, // DEGISTI
            CoverImageUrl = request.CoverImageUrl,
            Description = request.Description,
            PageCount = request.PageCount,
            PublishedYear = request.PublishedYear,
            Genre = request.Genre
        };

        await _repo.AddAsync(book);
        return ToResponse(book);
    }

    public async Task<BookResponse?> UpdateAsync(int id, UpdateBookRequest request)
    {
        var book = await _repo.GetByIdAsync(id);
        if (book is null) return null;

        // DEGISTI: ISBN doluysa temizle, dogrula, ISBN-13'e cevir
        var normalizedIsbn = string.IsNullOrWhiteSpace(request.Isbn)
            ? null
            : IsbnHelper.Normalize(request.Isbn);

        if (normalizedIsbn is not null)
        {
            var existing = await _repo.GetByIsbnAsync(normalizedIsbn);
            if (existing is not null && existing.Id != id)
            {
                throw new InvalidOperationException("Bu ISBN başka bir kitapta kayıtlı.");
            }
        }

        book.Title = request.Title.Trim();
        book.Author = request.Author.Trim();
        book.Isbn = normalizedIsbn; // DEGISTI
        book.CoverImageUrl = request.CoverImageUrl;
        book.Description = request.Description;
        book.PageCount = request.PageCount;
        book.PublishedYear = request.PublishedYear;
        book.Genre = request.Genre;

        await _repo.UpdateAsync(book);
        return ToResponse(book);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var book = await _repo.GetByIdAsync(id);
        if (book is null) return false;

        if (await _repo.IsInAnyLibraryAsync(id))
        {
            throw new InvalidOperationException("Bu kitap kullanıcıların kitaplığında olduğu için silinemez.");
        }

        await _repo.DeleteAsync(book);
        return true;
    }

    public async Task<BookResponse> FindOrFetchByIsbnAsync(string isbn)
    {
        // DEGISTI: Trim yerine tam normalizasyon (temizle, dogrula, ISBN-13)
        var normalizedIsbn = IsbnHelper.Normalize(isbn);

        var existingBook = await _repo.GetByIsbnAsync(normalizedIsbn);
        if (existingBook is not null)
        {
            return ToResponse(existingBook);
        }

        var externalBook = await _lookupService.SearchByIsbnAsync(normalizedIsbn);
        if (externalBook is null)
        {
                       throw new KeyNotFoundException("Bu ISBN ile eşleşen bir kitap bulunamadı. Kitabı manuel ekleyebilirsiniz.");
        }

        var newBook = new Book
        {
            Title = externalBook.Title,
            Author = externalBook.Author,
            Isbn = normalizedIsbn, // DEGISTI: aranan ISBN kaydedilir, ayni ISBN tekrar aranirsa DB'de bulunur
            CoverImageUrl = externalBook.CoverImageUrl,
            Description = externalBook.Description,
            PageCount = externalBook.PageCount,
            PublishedYear = externalBook.PublishedYear,
            Genre = externalBook.Genre
        };

        await _repo.AddAsync(newBook);

        return ToResponse(newBook);
    }

    private static BookResponse ToResponse(Book b) => new()
    {
        Id = b.Id,
        Title = b.Title,
        Author = b.Author,
        Isbn = b.Isbn,
        CoverImageUrl = b.CoverImageUrl,
        Description = b.Description,
        PageCount = b.PageCount,
        PublishedYear = b.PublishedYear,
        Genre = b.Genre
    };
}