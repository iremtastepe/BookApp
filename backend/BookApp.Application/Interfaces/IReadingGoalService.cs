using BookApp.Application.DTOs.Goals;

namespace BookApp.Application.Interfaces;

public interface IReadingGoalService
{
    // Yıllık hedef oluşturur veya mevcutsa günceller (Upsert)
    Task<AnnualReadingGoalDto> CreateOrUpdateGoalAsync(int userId, int year, CreateOrUpdateReadingGoalRequest request);

    // Belirli bir yılın okuma hedefini, okunan kitap sayısını ve ilerleme durumunu getirir
    Task<AnnualReadingGoalDto> GetAnnualGoalAsync(int userId, int year);

    // Belirli bir yıl ve ayda okunan kitapların listesini ve istatistiğini getirir (sayfalama destekli)
    Task<MonthlyReadSummaryDto> GetMonthlySummaryAsync(int userId, int year, int month, int page, int pageSize);
}
