using BookApp.Domain.Enums;

namespace BookApp.Domain.Entities;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Description { get; set; }
    public int? PageCount { get; set; }
    public int? PublishedYear { get; set; }
    public string? Genre { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.NotStarted;
    public bool IsFavorite { get; set; } = false;
    public int? Rating { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User? User { get; set; }

    public List<Note> Notes { get; set; } = new();
}