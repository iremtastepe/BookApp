using BookApp.Application.DTOs.Statistics;

namespace BookApp.Application.Interfaces;

public interface IStatisticsService
{
    // Genel özet: toplam kitap, 5 durumun dağılımı, favori sayısı, en çok okunan 5 yazar ve tür,
    // en yüksek puanlı 5 kitap ve puan dağılımı (yazar, tür ve puan sadece "Read" kitaplardan)
    Task<OverviewStatisticsDto> GetOverviewAsync(int userId);

    // Seçilen ayın analizi (tamamlanma tarihine göre).
    // Yıl DateValidator.MinYear - MaxYear (2000 - bu yıl + 1), ay 1-12 dışındaysa ArgumentException (400)
    Task<MonthlyStatisticsDto> GetMonthlyAsync(int userId, int year, int month);

    // Yıllara göre karşılaştırma ve seçilen yılın 12 aylık dağılımı.
    // Yıl DateValidator.MinYear - MaxYear (2000 - bu yıl + 1) dışındaysa ArgumentException (400)
    Task<YearlyStatisticsDto> GetYearlyAsync(int userId, int year);
}