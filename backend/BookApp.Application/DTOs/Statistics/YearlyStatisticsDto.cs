namespace BookApp.Application.DTOs.Statistics;

using System.Collections.Generic;

// GET /api/Statistics/yearly?year=2026 cevabı
public class YearlyStatisticsDto
{
    // Seçilen yıl (aşağıdaki Months bu yıla aittir)
    public int Year { get; set; }

    // Yıllara göre karşılaştırma: SADECE kitap tamamlanan yıllar listelenir, yıla göre artan sırada
    public List<YearCountDto> Years { get; set; } = new();

    // Seçilen yılın 12 ayı, her zaman 12 satır. Yıl boşsa hepsi 0.
    public List<MonthCountDto> Months { get; set; } = new();

    // Seçilen yılda biten ama ayı bilinmeyen (yalnızca yıl girilmiş) kitap sayısı.
    // Months toplamı + UnknownMonthCount = o yılın toplam kitap sayısı
    public int UnknownMonthCount { get; set; }

    // Hiçbir tarihi olmayan Read kitap sayısı. Bu kitaplar hiçbir yıla veya aya girmez,
    // bu yüzden Years toplamı Read kitap toplamından küçük çıkabilir; fark buradan anlaşılır.
    public int UndatedReadCount { get; set; }
}
