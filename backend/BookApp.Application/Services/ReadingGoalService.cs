using BookApp.Application.DTOs.Goals;
using BookApp.Application.Helpers;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;

namespace BookApp.Application.Services;

public class ReadingGoalService : IReadingGoalService
{
    // Yıllık kitap hedefi için kabul edilen aralık
    private const int MinTargetBooks = 1;
    private const int MaxTargetBooks = 1000;

    private readonly IReadingGoalRepository _readingGoalRepository;
    private readonly IUserBookRepository _userBookRepository;

    public ReadingGoalService(
        IReadingGoalRepository readingGoalRepository,
        IUserBookRepository userBookRepository)
    {
        _readingGoalRepository = readingGoalRepository;
        _userBookRepository = userBookRepository;
    }

    public async Task<AnnualReadingGoalDto> CreateOrUpdateGoalAsync(int userId, int year, CreateOrUpdateReadingGoalRequest request)
    {
        DateValidator.ValidateYearRange(year);
        ValidateTargetBooks(request.TargetBooks);

        var existingGoal = await _readingGoalRepository.GetByUserAndYearAsync(userId, year);
        if (existingGoal is not null)
        {
            existingGoal.TargetBooks = request.TargetBooks;
            existingGoal.UpdatedAt = DateTime.UtcNow;
            await _readingGoalRepository.UpdateAsync(existingGoal);
        }
        else
        {
            var newGoal = new ReadingGoal
            {
                UserId = userId,
                Year = year,
                TargetBooks = request.TargetBooks,
                CreatedAt = DateTime.UtcNow
            };
            await _readingGoalRepository.AddAsync(newGoal);
        }

        return await BuildAnnualGoalDtoAsync(userId, year, request.TargetBooks);
    }

    public async Task<AnnualReadingGoalDto> GetAnnualGoalAsync(int userId, int year)
    {
        DateValidator.ValidateYearRange(year);

        var goal = await _readingGoalRepository.GetByUserAndYearAsync(userId, year);

        return await BuildAnnualGoalDtoAsync(userId, year, goal?.TargetBooks);
    }

    public async Task<MonthlyReadSummaryDto> GetMonthlySummaryAsync(int userId, int year, int month, int page, int pageSize)
    {
        DateValidator.ValidateYearRange(year);
        DateValidator.ValidateMonth(month);

        if (page < 1)
            throw new ArgumentException("page 1 veya daha büyük olmalıdır.");

        if (pageSize < 1 || pageSize > 50)
            throw new ArgumentException("pageSize 1 ile 50 arasında olmalıdır.");

        var (items, totalCount) = await _userBookRepository.GetMonthlyReadBooksPagedAsync(userId, year, month, page, pageSize);

        return new MonthlyReadSummaryDto
        {
            Year = year,
            Month = month,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Books = items.Select(ub => new MonthlyReadBookDto
            {
                UserBookId = ub.Id,
                BookId = ub.BookId,
                Title = ub.Book.Title,
                Author = ub.Book.Author,
                CoverImageUrl = ub.Book.CoverImageUrl,
                FinishedYear = ub.FinishedYear,
                FinishedMonth = ub.FinishedMonth,
                FinishedDay = ub.FinishedDay,
                Rating = ub.Rating,
                Review = ub.Review
            }).ToList()
        };
    }

    // Hedef kitap sayısı iş kuralı burada kontrol edilir (DTO'da [Range] yok: tek kaynak bu metot)
    private static void ValidateTargetBooks(int targetBooks)
    {
        if (targetBooks < MinTargetBooks || targetBooks > MaxTargetBooks)
            throw new ArgumentException($"Hedef kitap sayısı {MinTargetBooks} ile {MaxTargetBooks} arasında olmalıdır.");
    }

    private async Task<AnnualReadingGoalDto> BuildAnnualGoalDtoAsync(int userId, int year, int? targetBooks)
    {
        var readBooksCount = await _userBookRepository.GetReadBooksCountByYearAsync(userId, year);
        var totalReadBooks = await _userBookRepository.GetTotalReadBooksCountAsync(userId);

        double? percentage = null;
        int? remainingBooks = null;

        if (targetBooks.HasValue && targetBooks.Value > 0)
        {
            percentage = Math.Round((double)readBooksCount / targetBooks.Value * 100, 1);
            remainingBooks = Math.Max(0, targetBooks.Value - readBooksCount);
        }

        return new AnnualReadingGoalDto
        {
            Year = year,
            TargetBooks = targetBooks,
            ReadBooksCount = readBooksCount,
            Percentage = percentage,
            RemainingBooks = remainingBooks,
            TotalReadBooks = totalReadBooks
        };
    }
}