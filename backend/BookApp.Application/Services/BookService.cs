using BookApp.Application.DTOs.Books;
using BookApp.Application.Exceptions;
using BookApp.Application.Helpers;
using BookApp.Application.Interfaces;
using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;

namespace BookApp.Application.Services;

public class BookService : IBookService
{
    private const int MaxTitleLength = 300;
    private const int MaxAuthorLength = 200;
    private const int MaxCoverImageUrlLength = 500;
    private const int MaxDescriptionLength = 2000;
    private const int MaxGenreLength = 100;
    private const int MaxPageCount = 10000;
    private const int MaxPublishedYear = 2100;

    private readonly IBookRepository _repo;
    private readonly IBookLookupService _lookupService;

    public BookService(
        IBookRepository repo,
        IBookLookupService lookupService)
    {
        _repo = repo;
        _lookupService = lookupService;
    }

    public async Task<PagedResponse<BookResponse>> GetBooksAsync(
        string? search,
        int page,
        int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (items, total) = await _repo.GetPagedAsync(
            search,
            page,
            pageSize);

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

        return book is null
            ? null
            : ToResponse(book);
    }

    public async Task<BookResponse> CreateAsync(
        int userId,
        CreateBookRequest request)
    {
        if (userId <= 0)
        {
            throw new ArgumentException(
                "Kitap oluşturmak için geçerli bir kullanıcı kimliği gereklidir.",
                nameof(userId));
        }

        // ISBN doluysa temizle, dogrula ve ISBN-13'e cevir.
        var normalizedIsbn = string.IsNullOrWhiteSpace(request.Isbn)
            ? null
            : IsbnHelper.Normalize(request.Isbn);

        if (normalizedIsbn is not null &&
            await _repo.GetByIsbnAsync(normalizedIsbn) is not null)
        {
            throw new InvalidOperationException(
                "Bu ISBN ile kayıtlı bir kitap zaten var.");
        }

        var coverUrl = ValidateManualCoverUrl(
            request.CoverImageUrl);

        var description = ValidateManualDescription(
            request.Description);

        var book = new Book
        {
            Title = request.Title.Trim(),
            Author = request.Author.Trim(),
            Isbn = normalizedIsbn,
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = request.PageCount,
            PublishedYear = request.PublishedYear,
            Genre = request.Genre,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            CreatedSource = BookCreationSource.Manual
        };

        await _repo.AddAsync(book);

        return ToResponse(book);
    }

    public async Task<BookResponse?> UpdateAsync(
        int id,
        UpdateBookRequest request)
    {
        var book = await _repo.GetByIdAsync(id);

        if (book is null)
        {
            return null;
        }

        // ISBN doluysa temizle, dogrula ve ISBN-13'e cevir.
        var normalizedIsbn = string.IsNullOrWhiteSpace(request.Isbn)
            ? null
            : IsbnHelper.Normalize(request.Isbn);

        if (normalizedIsbn is not null)
        {
            var existing = await _repo.GetByIsbnAsync(
                normalizedIsbn);

            if (existing is not null && existing.Id != id)
            {
                throw new InvalidOperationException(
                    "Bu ISBN başka bir kitapta kayıtlı.");
            }
        }

        // Mevcut hatali URL aynen gonderilmisse koru.
        // Degistirilen URL'yi ise dogrula.
        var coverUrl = string.Equals(
            request.CoverImageUrl,
            book.CoverImageUrl,
            StringComparison.Ordinal)
                ? book.CoverImageUrl
                : ValidateManualCoverUrl(request.CoverImageUrl);

        var description = ValidateManualDescription(
            request.Description);

        book.Title = request.Title.Trim();
        book.Author = request.Author.Trim();
        book.Isbn = normalizedIsbn;
        book.CoverImageUrl = coverUrl;
        book.Description = description;
        book.PageCount = request.PageCount;
        book.PublishedYear = request.PublishedYear;
        book.Genre = request.Genre;
        // 7.4-C.5: CreatedByUserId, CreatedAt ve CreatedSource degistirilmez, aynen korunur.

        await _repo.UpdateAsync(book);

        return ToResponse(book);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var book = await _repo.GetByIdAsync(id);

        if (book is null)
        {
            return false;
        }

        if (await _repo.IsInAnyLibraryAsync(id))
        {
            throw new InvalidOperationException(
                "Bu kitap kullanıcıların kitaplığında olduğu için silinemez.");
        }

        await _repo.DeleteAsync(book);

        return true;
    }

    public async Task<BookResponse> FindOrFetchByIsbnAsync(
        int userId,
        string isbn)
    {
        if (userId <= 0)
        {
            throw new ArgumentException(
                "ISBN ile kitap aramak veya eklemek için geçerli bir kullanıcı kimliği gereklidir.",
                nameof(userId));
        }

        // ISBN'yi temizle, dogrula ve ISBN-13'e cevir.
        var normalizedIsbn = IsbnHelper.Normalize(isbn);

        var existingBook = await _repo.GetByIsbnAsync(
            normalizedIsbn);

        if (existingBook is not null)
        {
            return ToResponse(existingBook);
        }

        var externalBook = await _lookupService.SearchByIsbnAsync(
            normalizedIsbn);

        if (externalBook is null)
        {
            throw new KeyNotFoundException(
                "Bu ISBN ile eşleşen bir kitap bulunamadı. " +
                "Kitabı manuel ekleyebilirsiniz.");
        }

        // Dis API'den gelen alanlari kaydetmeden once
        // uygulamanin veri kurallarina uygun hale getir.
        var newBook = new Book
        {
            Title = NormalizeExternalRequiredText(
                externalBook.Title,
                "Bilinmeyen Kitap",
                MaxTitleLength),

            Author = NormalizeExternalRequiredText(
                externalBook.Author,
                "Bilinmeyen Yazar",
                MaxAuthorLength),

            Isbn = normalizedIsbn,

            CoverImageUrl = NormalizeExternalCoverUrl(
                externalBook.CoverImageUrl),

            Description = NormalizeExternalDescription(
                externalBook.Description),

            PageCount = externalBook.PageCount is >= 1 and <= MaxPageCount
                ? externalBook.PageCount
                : null,

            PublishedYear = externalBook.PublishedYear is >= 1 and <= MaxPublishedYear
                ? externalBook.PublishedYear
                : null,

            Genre = NormalizeExternalOptionalText(
                externalBook.Genre,
                MaxGenreLength),

            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            CreatedSource = BookCreationSource.ExternalLookup
        };

        try
        {
            await _repo.AddAsync(newBook);

            return ToResponse(newBook);
        }
        catch (BookIsbnAlreadyExistsException)
        {
            // Baska bir istek ayni ISBN'li kitabi bizden
            // once eklemis olabilir.
            var concurrentBook = await _repo.GetByIsbnAsync(
                normalizedIsbn);

            if (concurrentBook is not null)
            {
                return ToResponse(concurrentBook);
            }

            // ISBN cakismasi olustu ancak kayit bulunamadi.
            // Beklenmeyen hatayi gizleme.
            throw;
        }
    }

    // Manuel kitaplarda kapak URL dogrulamasi.
    private static string? ValidateManualCoverUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var url = value.Trim();

        if (url.Length > MaxCoverImageUrlLength ||
            !IsValidHttpUrl(url))
        {
            throw new ArgumentException(
                "Kapak görseli adresi en fazla 500 karakter " +
                "uzunluğunda, geçerli bir HTTP veya HTTPS URL'si olmalıdır.");
        }

        return url;
    }

    // Dis API'den gelen gecersiz kapak URL'lerini yok say.
    private static string? NormalizeExternalCoverUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var url = value.Trim();

        if (url.Length > MaxCoverImageUrlLength ||
            !IsValidHttpUrl(url))
        {
            return null;
        }

        return url;
    }

    private static bool IsValidHttpUrl(string value)
    {
        return Uri.TryCreate(
                   value,
                   UriKind.Absolute,
                   out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp ||
                   uri.Scheme == Uri.UriSchemeHttps);
    }

    // Manuel kitap aciklamasi en fazla 2000 karakter olabilir.
    private static string? ValidateManualDescription(string? value)
    {
        if (value is not null &&
            value.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                "Kitap açıklaması en fazla 2000 karakter olabilir.");
        }

        return value;
    }

    // Dis API'den gelen uzun aciklamalari kisalt.
    private static string? NormalizeExternalDescription(string? value)
    {
        if (value is null ||
            value.Length <= MaxDescriptionLength)
        {
            return value;
        }

        return value[..MaxDescriptionLength];
    }

    // Dis API'den gelen zorunlu metin alanlarini duzenle.
    // Bos degerlerde varsayilan metin kullan.
    private static string NormalizeExternalRequiredText(
        string? value,
        string fallback,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim();

        return text.Length <= maxLength
            ? text
            : text[..maxLength].TrimEnd();
    }

    // Dis API'den gelen istege bagli metin alanlarini duzenle.
    // Bos degerleri null yap.
    private static string? NormalizeExternalOptionalText(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        return text.Length <= maxLength
            ? text
            : text[..maxLength].TrimEnd();
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
