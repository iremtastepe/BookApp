using BookApp.Domain.Enums;

namespace BookApp.Domain.Entities;

public class UserBook
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public ReadingStatus Status { get; set; } = ReadingStatus.NotStarted;
    public bool IsFavorite { get; set; } = false;
    public int? Rating { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? DidNotFinishAt { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}