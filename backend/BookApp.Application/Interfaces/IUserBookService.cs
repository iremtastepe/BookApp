using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;

namespace BookApp.Application.Interfaces;

public interface IUserBookService
{
    Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request);
    Task<PagedResponse<UserBookDto>> GetLibraryAsync(int userId, LibraryQuery query);
    Task<UserBookDto> UpdateStatusAsync(int userId, int userBookId, UpdateUserBookStatusRequest request);
    Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request);
    Task<UserBookDto> SetFavoriteAsync(int userId, int userBookId, SetFavoriteRequest request);
    Task<UserBookDto> UpdateProgressAsync(int userId, int userBookId, UpdateProgressRequest request);
}