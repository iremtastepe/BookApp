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
            AddedAt = DateTime.UtcNow
        };

        await _userBookRepository.AddAsync(userBook);

        return new UserBookDto
        {
            Id = userBook.Id,
            BookId = book.Id,
            Title = book.Title,
            Author = book.Author,
            CoverImageUrl = book.CoverImageUrl,
            Status = userBook.Status,
            IsFavorite = userBook.IsFavorite,
            Rating = userBook.Rating,
            StartedAt = userBook.StartedAt,
            FinishedAt = userBook.FinishedAt,
            AddedAt = userBook.AddedAt
        };
    }
}