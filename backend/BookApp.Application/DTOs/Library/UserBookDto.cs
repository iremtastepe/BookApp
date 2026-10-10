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
    public DateTime? FinishedAt { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? DidNotFinishAt { get; set; }
    public int? PageCount { get; set; }

}