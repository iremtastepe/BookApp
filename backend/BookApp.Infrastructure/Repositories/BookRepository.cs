using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Repositories;

public class BookRepository : IBookRepository
{
    private readonly AppDbContext _context;

    public BookRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<Book?> GetByIdAsync(int id)
    {
        return _context.Books.FirstOrDefaultAsync(b => b.Id == id);
    }

    public Task<Book?> GetByIsbnAsync(string isbn)
    {
        return _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Isbn == isbn);
    }

    public async Task<(List<Book> Items, int TotalCount)> GetPagedAsync(string? search, int page, int pageSize)
    {
        var query = _context.Books.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Title, pattern) ||
                EF.Functions.ILike(b.Author, pattern));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(b => b.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<bool> IsInAnyLibraryAsync(int bookId)
    {
        return _context.UserBooks.AnyAsync(ub => ub.BookId == bookId);
    }

    public async Task AddAsync(Book book)
    {
        _context.Books.Add(book);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Book book)
    {
        _context.Books.Update(book);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Book book)
    {
        _context.Books.Remove(book);
        await _context.SaveChangesAsync();
    }
}