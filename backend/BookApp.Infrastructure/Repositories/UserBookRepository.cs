using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Repositories;

public class UserBookRepository : IUserBookRepository
{
    private readonly AppDbContext _context;

    public UserBookRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsAsync(int userId, int bookId)
        => _context.UserBooks.AnyAsync(ub => ub.UserId == userId && ub.BookId == bookId);

    public async Task<UserBook> AddAsync(UserBook userBook)
    {
        _context.UserBooks.Add(userBook);
        await _context.SaveChangesAsync();
        return userBook;
    }

    public Task<UserBook?> GetByIdWithBookAsync(int id, int userId)
        => _context.UserBooks
            .Include(ub => ub.Book)
            .FirstOrDefaultAsync(ub => ub.Id == id && ub.UserId == userId);

    public Task<List<UserBook>> GetAllByUserIdAsync(int userId)
        => _context.UserBooks
            .Include(ub => ub.Book)
            .Where(ub => ub.UserId == userId)
            .OrderByDescending(ub => ub.AddedAt)
            .ToListAsync();

    public Task UpdateAsync(UserBook userBook)
    {
        _context.UserBooks.Update(userBook);
        return _context.SaveChangesAsync();
    }
}