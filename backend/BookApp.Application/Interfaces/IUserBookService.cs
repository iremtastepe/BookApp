using BookApp.Application.DTOs.Library;

namespace BookApp.Application.Interfaces;

public interface IUserBookService
{
    Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request);
    Task<List<UserBookDto>> GetLibraryAsync(int userId);
    Task<UserBookDto> UpdateStatusAsync(int userId, int userBookId, UpdateUserBookStatusRequest request);
    Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request);
}