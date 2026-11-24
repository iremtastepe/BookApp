using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Repositories;

public class BookRepository : IBookRepository
{
    private readonly AppDbContext _context;

    private const string BookIsbnUniqueIndexName = "IX_Books_Isbn";

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
        return _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Isbn == isbn);
    }

    public async Task<(List<Book> Items, int TotalCount)> GetPagedAsync(
        string? search, int page, int pageSize)
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

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsBookIsbnUniqueViolation(ex))
        {
            // Basarisiz eklenen kitabi EF Core takibinden cikar.
            _context.Entry(book).State = EntityState.Detached;

            throw new BookIsbnAlreadyExistsException(ex);
        }
    }

    public async Task UpdateAsync(Book book)
    {
        var entry = _context.Entry(book);

        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "Detached nesneler doğrudan güncellenemez. Güncelleme için izlenen (tracked) entity kullanılmalıdır.");
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsBookIsbnUniqueViolation(ex))
        {
            // Basarisiz guncellemeyi EF Core takibinden cikar.
            entry.State = EntityState.Detached;

            throw new BookIsbnAlreadyExistsException(ex);
        }
    }

    // Yalnizca Books.Isbn benzersizlik indeksine ait hatayi yakala.
    private static bool IsBookIsbnUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pg
            && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
            && pg.ConstraintName == BookIsbnUniqueIndexName;
    }

    public async Task<int?> GetMaxCurrentPageAsync(int bookId)
    {
        return await _context.UserBooks
            .Where(ub => ub.BookId == bookId && ub.CurrentPage != null)
            .MaxAsync(ub => (int?)ub.CurrentPage);
    }

    public async Task<int?> GetMaxNonReadCurrentPageAsync(int bookId)
    {
        return await _context.UserBooks
            .Where(ub =>
                ub.BookId == bookId &&
                ub.Status != BookApp.Domain.Enums.ReadingStatus.Read &&
                ub.CurrentPage != null)
            .MaxAsync(ub => (int?)ub.CurrentPage);
    }

    public async Task<List<UserBook>> GetReadUserBooksByBookIdAsync(int bookId)
    {
        return await _context.UserBooks
            .Where(ub =>
                ub.BookId == bookId &&
                ub.Status == BookApp.Domain.Enums.ReadingStatus.Read)
            .ToListAsync();
    }

    public async Task DeleteAsync(Book book)
    {
        _context.Books.Remove(book);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException pgEx &&
            pgEx.SqlState == "23503")
        {
            throw new InvalidOperationException(
                "Bu kitap kullanıcıların kitaplığında olduğu için silinemez.", ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException != null &&
            ex.InnerException.Message.Contains(
                "foreign key", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Bu kitap kullanıcıların kitaplığında olduğu için silinemez.", ex);
        }
    }
}
