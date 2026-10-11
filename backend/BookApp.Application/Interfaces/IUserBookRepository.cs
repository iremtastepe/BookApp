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

    // Yıllık okunan kitap sayısı (Status == Read ve FinishedYear == year)
    Task<int> GetReadBooksCountByYearAsync(int userId, int year);

    // Kullanıcının tarihe bakılmaksızın okuduğu toplam kitap sayısı (Status == Read)
    Task<int> GetTotalReadBooksCountAsync(int userId);

    // Seçilen yılda ve ayda okunan kitapların listesi (sayfalama destekli) ve toplam sayısı
    Task<(List<UserBook> Items, int TotalCount)> GetMonthlyReadBooksPagedAsync(int userId, int year, int month, int page, int pageSize);
}