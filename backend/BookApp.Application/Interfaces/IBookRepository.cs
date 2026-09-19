using BookApp.Domain.Entities;

namespace BookApp.Application.Interfaces;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(int id);
    Task<Book?> GetByIsbnAsync(string isbn);
    Task<(List<Book> Items, int TotalCount)> GetPagedAsync(string? search, int page, int pageSize);
    Task<bool> IsInAnyLibraryAsync(int bookId);
    Task AddAsync(Book book);
    Task UpdateAsync(Book book);
    Task DeleteAsync(Book book);
}