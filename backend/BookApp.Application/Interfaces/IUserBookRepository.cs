using BookApp.Application.DTOs.Library;
using BookApp.Domain.Entities;

namespace BookApp.Application.Interfaces;

public interface IUserBookRepository
{
    Task<bool> ExistsAsync(int userId, int bookId);
    Task<UserBook> AddAsync(UserBook userBook);
    Task<UserBook?> GetByIdWithBookAsync(int id, int userId);
    Task<List<UserBook>> GetAllByUserIdAsync(int userId);
    Task<(List<UserBook> Items, int TotalCount)> GetPagedByUserIdAsync(int userId, LibraryQuery query);
    Task UpdateAsync(UserBook userBook);
}