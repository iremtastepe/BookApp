namespace BookApp.Application.DTOs.Goals;

public class MonthlyReadBookDto
{
    public int UserBookId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }

    public int? FinishedYear { get; set; }
    public int? FinishedMonth { get; set; }
    public int? FinishedDay { get; set; }

    // Tam tarih (gün) biliniyorsa hesaplanan tarih; bilinmiyorsa null
    public DateOnly? FinishedDate => (FinishedYear.HasValue && FinishedMonth.HasValue && FinishedDay.HasValue)
        ? new DateOnly(FinishedYear.Value, FinishedMonth.Value, FinishedDay.Value)
        : null;

    public decimal? Rating { get; set; }
    public string? Review { get; set; }

    // 7.1: null: kitap aktif kitaplıkta, dolu: kitap arşivde (arşive kaldırıldığı UTC tarih).
    // Arşivli "Okundu" kitaplar aylık listede ve sayıda yer almaya devam eder; bu alan yalnızca işaretleme içindir.
    public DateTime? ArchivedAt { get; set; }
}
