using BookApp.Application.DTOs.Books;

namespace BookApp.Application.Interfaces;

public interface IBookService
{
    Task<PagedResponse<BookResponse>> GetBooksAsync(string? search, int page, int pageSize);
    Task<BookResponse?> GetByIdAsync(int id);
    Task<BookResponse> CreateAsync(int userId, CreateBookRequest request);
    Task<BookResponse?> UpdateAsync(int id, UpdateBookRequest request);
    Task<bool> DeleteAsync(int id);
    Task<BookResponse> FindOrFetchByIsbnAsync(int userId, string isbn);
}
