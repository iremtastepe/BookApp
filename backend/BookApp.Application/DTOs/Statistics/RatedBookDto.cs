namespace BookApp.Application.DTOs.Statistics;

// "En yüksek/en düşük puanlı kitap" listelerinde bir kitabı temsil eder.
// Yorum (Review) bilerek yok: istatistik ekranında gerekmiyor, yanıtı şişirmesin.
public class RatedBookDto
{
    public int UserBookId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public decimal Rating { get; set; }

    // Tamamlanma tarihi parçaları (kısmi olabilir, hepsi null ise tarih bilinmiyor)
    public int? FinishedYear { get; set; }
    public int? FinishedMonth { get; set; }
    public int? FinishedDay { get; set; }
}
