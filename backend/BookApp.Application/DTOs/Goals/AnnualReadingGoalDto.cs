namespace BookApp.Application.DTOs.Goals;

public class AnnualReadingGoalDto
{
    public int Year { get; set; }

    // Kullanıcının belirlediği hedef (hedef yoksa null)
    public int? TargetBooks { get; set; }

    // Bu takvim yılında bitirilen kitap sayısı
    public int ReadBooksCount { get; set; }

    // İlerleme yüzdesi (hedef yoksa null; hedef aşıldığında %100'ün üzerine çıkabilir)
    public double? Percentage { get; set; }

    // Hedefe kalan kitap sayısı (hedef yoksa null; hedef aşıldıysa 0)
    public int? RemainingBooks { get; set; }

    // Kullanıcının kitaplığındaki (tarihi bilinen veya bilinmeyen) TÜM "Read" durumundaki kitapların toplamı
    public int TotalReadBooks { get; set; }
}
