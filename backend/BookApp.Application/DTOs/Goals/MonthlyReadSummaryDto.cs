namespace BookApp.Application.DTOs.Goals;

public class MonthlyReadSummaryDto
{
    public int Year { get; set; }
    public int Month { get; set; }

    // Seçilen ayda bitirilen toplam kitap sayısı (sayfalamadan bağımsız genel toplam)
    public int TotalCount { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    // İlgili sayfadaki kitapların listesi
    public List<MonthlyReadBookDto> Books { get; set; } = new();
}
