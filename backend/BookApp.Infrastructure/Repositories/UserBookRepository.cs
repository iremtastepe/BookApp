using BookApp.Application.DTOs.Library;
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

    public async Task<(List<UserBook> Items, int TotalCount)> GetPagedByUserIdAsync(int userId, LibraryQuery query)
    {
        // Sorgu HER ZAMAN kullanıcının kendi kayıtlarıyla başlar; sonraki filtreler bunun üstüne eklenir.
        // Böylece hiçbir filtre kombinasyonu başkasının kitaplığını getiremez.
        var userBooks = _context.UserBooks
            .AsNoTracking() // sadece okuyoruz, EF'in değişiklik takibine gerek yok
            .Include(ub => ub.Book)
            .Where(ub => ub.UserId == userId);

        // Arama: GET /api/Books ile aynı yöntem (ILike = Postgres'te büyük/küçük harf duyarsız LIKE)
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            userBooks = userBooks.Where(ub =>
                EF.Functions.ILike(ub.Book.Title, pattern) ||
                EF.Functions.ILike(ub.Book.Author, pattern));
        }

        // Durum filtresi: liste doluysa "bu durumlardan herhangi biri" (SQL'de IN / ANY)
        if (query.Status.Count > 0)
        {
            var statuses = query.Status;
            userBooks = userBooks.Where(ub => statuses.Contains(ub.Status));
        }

        if (query.IsFavorite.HasValue)
        {
            var isFavorite = query.IsFavorite.Value;
            userBooks = userBooks.Where(ub => ub.IsFavorite == isFavorite);
        }

        // Toplam sayı, sayfalamadan ÖNCE hesaplanır: "bu filtreyle toplam kaç kitap var?"
        var total = await userBooks.CountAsync();

        var isDesc = query.SortDir == LibrarySortDirection.Desc;

        IOrderedQueryable<UserBook> ordered = query.SortBy switch
        {
            LibrarySortBy.Title => isDesc
    ? userBooks.OrderByDescending(ub =>
        EF.Functions.Collate(ub.Book.Title, "tr-x-icu"))
    : userBooks.OrderBy(ub =>
        EF.Functions.Collate(ub.Book.Title, "tr-x-icu")),

            // PostgreSQL'de NULL'lar artan sıralamada sonda, azalanda başta gelir.
            // Puansız kitapların her iki yönde de sonda kalması için önce "puanı boş mu?" diye sıralarız
            // (false < true olduğu için puanı olanlar öne geçer), sonra puana göre.
            LibrarySortBy.Rating => isDesc
                ? userBooks.OrderBy(ub => ub.Rating == null).ThenByDescending(ub => ub.Rating)
                : userBooks.OrderBy(ub => ub.Rating == null).ThenBy(ub => ub.Rating),

            _ => isDesc
                ? userBooks.OrderByDescending(ub => ub.AddedAt)
                : userBooks.OrderBy(ub => ub.AddedAt)
        };

        // İkinci ölçüt olarak Id: aynı puana/ada/tarihe sahip kayıtların sırası her istekte aynı kalır,
        // yoksa sayfalar arasında bir kitap iki kez çıkabilir veya hiç çıkmayabilir.
        var items = await ordered
            .ThenBy(ub => ub.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task UpdateAsync(UserBook userBook)
    {
        _context.UserBooks.Update(userBook);
        return _context.SaveChangesAsync();
    }
}