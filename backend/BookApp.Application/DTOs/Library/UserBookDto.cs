using BookApp.Application.Helpers;
using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Library;

public class UserBookDto
{
    public int Id { get; set; }            // UserBook Id'si
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public ReadingStatus Status { get; set; }
    public bool IsFavorite { get; set; }
    public decimal? Rating { get; set; }
    public string? Review { get; set; }
    public int? CurrentPage { get; set; }           // null: ilerleme girilmemiş, 0: henüz ilerlememiş
    public double? ProgressPercentage { get; set; } // null: hesaplanamıyor (toplam sayfa bilinmiyor veya ilerleme girilmemiş)
    public DateTime? StartedAt { get; set; }
    public int? FinishedYear { get; set; }
    public int? FinishedMonth { get; set; }
    public int? FinishedDay { get; set; }
    // Tam tarih (gün, ay, yıl) biliniyorsa hesaplanan tarih; bilinmiyorsa null (asla sahte gün üretilmez)
    public DateOnly? FinishedDate => (FinishedYear.HasValue && FinishedMonth.HasValue && FinishedDay.HasValue)
        ? new DateOnly(FinishedYear.Value, FinishedMonth.Value, FinishedDay.Value)
        : null;

    // StartedAt UTC DateTime olarak saklanır; takvim günü saat diliminden bağımsız olarak hesaplanır
    public DateOnly? StartedDate => StartedAt.HasValue
        ? DateValidator.ToUtcCalendarDate(StartedAt.Value)
        : null;

    public DateTime AddedAt { get; set; }
    public DateTime? DidNotFinishAt { get; set; }
    public int? PageCount { get; set; }
}
