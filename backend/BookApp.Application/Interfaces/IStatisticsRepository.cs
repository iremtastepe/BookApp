using BookApp.Application.DTOs.Statistics;
using BookApp.Domain.Enums;

namespace BookApp.Application.Interfaces;

public interface IStatisticsRepository
{
    // ---- Genel özet (tüm kitaplar) ----

    // Her durumdan kaç kitap olduğu. Kitabı olmayan durum sözlükte YOKTUR (sıfırları Service doldurur).
    Task<Dictionary<ReadingStatus, int>> GetStatusCountsAsync(int userId);

    Task<int> GetFavoriteCountAsync(int userId);

    // ---- Sadece "Read" kitaplar. year/month verilmezse tüm zamanlar, verilirse o dönem ----

    // Tamamlanan kitap sayısı
    Task<int> GetReadCountAsync(int userId, int? year = null, int? month = null);

    // Yazar ve tür sayıları HAM döner: boşluklar kırpılmış ama "Işık" ile "ışık" ayrı gruplardır.
    // Büyük/küçük harf birleştirme, "Bilinmeyen Yazar" ayıklama ve ilk N seçimi Service'in işidir.
    Task<List<NameCountDto>> GetReadAuthorCountsAsync(int userId, int? year = null, int? month = null);
    Task<List<NameCountDto>> GetReadGenreCountsAsync(int userId, int? year = null, int? month = null);

    // Türü boş veya sadece boşluk olan okunmuş kitap sayısı
    Task<int> GetReadGenreMissingCountAsync(int userId, int? year = null, int? month = null);

    // ---- Puanlar (sadece puanlı ve Read kitaplar) ----

    // Puanı en yüksek N kitap (eşitlikte yeni tamamlanan önce, tarihsizler sonda)
    Task<List<RatedBookDto>> GetTopRatedReadBooksAsync(int userId, int take);

    // Seçilen ayda tamamlanan puanlı kitapların HEPSİ (en yüksek/en düşük ve eşitleri Service seçer)
    Task<List<RatedBookDto>> GetRatedReadBooksAsync(int userId, int year, int month);

    // Her puan değeri için kitap sayısı. Dolu kovalar döner, 9 kovaya tamamlama Service'in işi.
    Task<List<RatingBucketDto>> GetReadRatingCountsAsync(int userId);

    // ---- Yıllık / aylık dağılım ----

    // Yıllara göre sayı, sadece verisi olan yıllar, yıla göre artan
    Task<List<YearCountDto>> GetReadCountsByYearAsync(int userId);

    // Seçilen yılın ay dağılımı, sadece ayı bilinen ve kitabı olan aylar
    Task<List<MonthCountDto>> GetReadCountsByMonthAsync(int userId, int year);

    // Seçilen yılda biten ama ayı bilinmeyen kitap sayısı
    Task<int> GetReadUnknownMonthCountAsync(int userId, int year);

    // Hiçbir bitiş tarihi olmayan Read kitap sayısı
    Task<int> GetUndatedReadCountAsync(int userId);
}