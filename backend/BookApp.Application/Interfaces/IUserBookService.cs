using BookApp.Application.DTOs.Library;

namespace BookApp.Application.Interfaces;

public interface IUserBookService
{
    Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request);
}