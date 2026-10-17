using System.Linq.Expressions;
using BookApp.Application.DTOs.Statistics;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Repositories;

public class StatisticsRepository : IStatisticsRepository
{
    private readonly AppDbContext _context;

    public StatisticsRepository(AppDbContext context)
    {
        _context = context;
    }

    // UserBook -> RatedBookDto dönüşümü. Expression olduğu için EF bunu SQL'e çevirir,
    // yani sadece bu alanlar veritabanından çekilir (Include gerekmez).
    // Çağıran sorgu, Rating'in null olmadığını daha önce Where ile garanti etmelidir.
    private static readonly Expression<Func<UserBook, RatedBookDto>> ToRatedBookDto = ub => new RatedBookDto
    {
        UserBookId = ub.Id,
        BookId = ub.BookId,
        Title = ub.Book.Title,
        Author = ub.Book.Author,
        CoverImageUrl = ub.Book.CoverImageUrl,
        Rating = ub.Rating!.Value,
        FinishedYear = ub.FinishedYear,
        FinishedMonth = ub.FinishedMonth,
        FinishedDay = ub.FinishedDay
    };

    // Kullanıcının "Read" kitapları; year/month verilirse tamamlanma tarihine göre daraltır.
    // Sorgu HER ZAMAN UserId ile başlar, başkasının kitaplığı hiçbir filtreyle gelemez.
    private IQueryable<UserBook> ReadBooks(int userId, int? year = null, int? month = null)
    {
        var query = _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read);

        if (year.HasValue)
        {
            var y = year.Value;
            query = query.Where(ub => ub.FinishedYear == y);
        }

        if (month.HasValue)
        {
            var m = month.Value;
            query = query.Where(ub => ub.FinishedMonth == m);
        }

        return query;
    }

    public Task<Dictionary<ReadingStatus, int>> GetStatusCountsAsync(int userId)
        => _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .GroupBy(ub => ub.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

    public Task<int> GetFavoriteCountAsync(int userId)
        => _context.UserBooks
            .AsNoTracking()
            .CountAsync(ub => ub.UserId == userId && ub.IsFavorite);

    public Task<int> GetReadCountAsync(int userId, int? year = null, int? month = null)
        => ReadBooks(userId, year, month).CountAsync();

    public Task<List<NameCountDto>> GetReadAuthorCountsAsync(int userId, int? year = null, int? month = null)
        => ReadBooks(userId, year, month)
            .GroupBy(ub => ub.Book.Author.Trim())
            .Select(g => new NameCountDto { Name = g.Key, Count = g.Count() })
            .ToListAsync();

    public Task<List<NameCountDto>> GetReadGenreCountsAsync(int userId, int? year = null, int? month = null)
        => ReadBooks(userId, year, month)
            .Where(ub => ub.Book.Genre != null && ub.Book.Genre.Trim() != "")
            .GroupBy(ub => ub.Book.Genre!.Trim())
            .Select(g => new NameCountDto { Name = g.Key, Count = g.Count() })
            .ToListAsync();

    public Task<int> GetReadGenreMissingCountAsync(int userId, int? year = null, int? month = null)
        => ReadBooks(userId, year, month)
            .CountAsync(ub => ub.Book.Genre == null || ub.Book.Genre.Trim() == "");

    public Task<List<RatedBookDto>> GetTopRatedReadBooksAsync(int userId, int take)
        => ReadBooks(userId)
            .Where(ub => ub.Rating != null)
            .OrderByDescending(ub => ub.Rating)
            // Eşit puanda: tarihi olanlar önce (false < true), sonra en yeni tamamlanan.
            // PostgreSQL'de DESC sıralamada NULL'lar başa geldiği için bu hile gerekli.
            .ThenBy(ub => ub.FinishedYear == null)
            .ThenByDescending(ub => ub.FinishedYear)
            .ThenBy(ub => ub.FinishedMonth == null)
            .ThenByDescending(ub => ub.FinishedMonth)
            .ThenBy(ub => ub.FinishedDay == null)
            .ThenByDescending(ub => ub.FinishedDay)
            .ThenBy(ub => ub.Id) // tam eşitlikte sıra her istekte aynı kalsın
            .Take(take)
            .Select(ToRatedBookDto)
            .ToListAsync();

    public Task<List<RatedBookDto>> GetRatedReadBooksAsync(int userId, int year, int month)
        => ReadBooks(userId, year, month)
            .Where(ub => ub.Rating != null)
            .Select(ToRatedBookDto)
            .ToListAsync();

    public Task<List<RatingBucketDto>> GetReadRatingCountsAsync(int userId)
        => ReadBooks(userId)
            .Where(ub => ub.Rating != null)
            .GroupBy(ub => ub.Rating!.Value)
            .Select(g => new RatingBucketDto { Rating = g.Key, Count = g.Count() })
            .ToListAsync();

    public Task<List<YearCountDto>> GetReadCountsByYearAsync(int userId)
        => ReadBooks(userId)
            .Where(ub => ub.FinishedYear != null)
            .GroupBy(ub => ub.FinishedYear!.Value)
            .Select(g => new YearCountDto { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToListAsync();

    public Task<List<MonthCountDto>> GetReadCountsByMonthAsync(int userId, int year)
        => ReadBooks(userId, year)
            .Where(ub => ub.FinishedMonth != null)
            .GroupBy(ub => ub.FinishedMonth!.Value)
            .Select(g => new MonthCountDto { Month = g.Key, Count = g.Count() })
            .OrderBy(x => x.Month)
            .ToListAsync();

    public Task<int> GetReadUnknownMonthCountAsync(int userId, int year)
        => ReadBooks(userId, year)
            .CountAsync(ub => ub.FinishedMonth == null);

    public Task<int> GetUndatedReadCountAsync(int userId)
        => ReadBooks(userId)
            .CountAsync(ub => ub.FinishedYear == null);
}