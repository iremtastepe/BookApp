namespace BookApp.Application.DTOs.Statistics;

using System.Collections.Generic;

// GET /api/Statistics/overview cevabı
public class OverviewStatisticsDto
{
    // Kitaplıktaki TÜM kitaplar (durum fark etmez)
    public int TotalBooks { get; set; }

    // 5 durumun hepsi her zaman listelenir, kitabı olmayan durum Count = 0 olur
    public List<StatusCountDto> StatusCounts { get; set; } = new();

    public int FavoriteCount { get; set; }

    // Bu üç bölüm ve aşağıdakiler SADECE "Read" kitaplardan hesaplanır
    public List<NameCountDto> TopAuthors { get; set; } = new();
    public List<NameCountDto> TopGenres { get; set; } = new();

    // Türü boş olan okunmuş kitap sayısı: kullanıcı tür listesinin neden eksik olduğunu görsün
    public int GenreMissingCount { get; set; }

    // En yüksek puanlı 5 kitap (puansızlar dahil edilmez)
    public List<RatedBookDto> TopRatedBooks { get; set; } = new();

    // Her zaman 9 kova: 1.0, 1.5, ... 5.0 (kitabı olmayan kovada Count = 0)
    public List<RatingBucketDto> RatingDistribution { get; set; } = new();
}
