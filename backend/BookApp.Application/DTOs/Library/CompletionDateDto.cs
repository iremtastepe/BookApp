namespace BookApp.Application.DTOs.Library;

/// <summary>
/// Kitabın tamamlanma tarihinin bileşenlerini (yıl, ay, gün) taşır.
/// Kısmi tarihleri (yalnızca yıl, ay-yıl veya tam gün) destekler.
/// </summary>
public class CompletionDateDto
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
}
