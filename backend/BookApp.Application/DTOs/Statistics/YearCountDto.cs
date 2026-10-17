namespace BookApp.Application.DTOs.Statistics;

// Yıllık karşılaştırmanın bir satırı: "2025 yılında 20 kitap tamamlandı"
public class YearCountDto
{
    public int Year { get; set; }
    public int Count { get; set; }
}
