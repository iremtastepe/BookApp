using BookApp.Application.DTOs.Library;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;

namespace BookApp.Application.Services;

public class UserBookService : IUserBookService
{
    private readonly IUserBookRepository _userBookRepository;
    private readonly IBookRepository _bookRepository;

    public UserBookService(IUserBookRepository userBookRepository, IBookRepository bookRepository)
    {
        _userBookRepository = userBookRepository;
        _bookRepository = bookRepository;
    }

    public async Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request)
    {
        var book = await _bookRepository.GetByIdAsync(request.BookId);
        if (book is null)
            throw new KeyNotFoundException("Kitap katalogda bulunamadı.");        // 404

        if (await _userBookRepository.ExistsAsync(userId, request.BookId))
            throw new InvalidOperationException("Bu kitap zaten kitaplığında.");  // 409

        var userBook = new UserBook
        {
            UserId = userId,
            BookId = request.BookId,
            Status = ReadingStatus.NotStarted,
            IsFavorite = false,
            AddedAt = DateTime.UtcNow,
            Book = book
        };

        await _userBookRepository.AddAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<List<UserBookDto>> GetLibraryAsync(int userId)
    {
        var userBooks = await _userBookRepository.GetAllByUserIdAsync(userId);
        return userBooks.Select(MapToDto).ToList();
    }

    public async Task<UserBookDto> UpdateStatusAsync(int userId, int userBookId, UpdateUserBookStatusRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");  // 404

        userBook.Status = request.Status;

        switch (request.Status)
        {
            case ReadingStatus.Reading:
                userBook.StartedAt = DateTime.UtcNow;
                break;
            case ReadingStatus.Read:
                userBook.FinishedAt = DateTime.UtcNow;
                break;
            case ReadingStatus.DidNotFinish:
                userBook.DidNotFinishAt = DateTime.UtcNow;
                break;
        }

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        userBook.Rating = request.Rating;
        userBook.Review = request.Review;

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    private static UserBookDto MapToDto(UserBook userBook) => new()
    {
        Id = userBook.Id,
        BookId = userBook.BookId,
        Title = userBook.Book.Title,
        Author = userBook.Book.Author,
        CoverImageUrl = userBook.Book.CoverImageUrl,
        Status = userBook.Status,
        IsFavorite = userBook.IsFavorite,
        Rating = userBook.Rating,
        Review = userBook.Review,
        StartedAt = userBook.StartedAt,
        FinishedAt = userBook.FinishedAt,
        DidNotFinishAt = userBook.DidNotFinishAt,
        AddedAt = userBook.AddedAt
    };
}