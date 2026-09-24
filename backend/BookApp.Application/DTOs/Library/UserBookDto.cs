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
    public int? Rating { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? DidNotFinishAt { get; set; }
}