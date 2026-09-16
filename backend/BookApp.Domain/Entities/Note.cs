namespace BookApp.Domain.Entities;

public class Note
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int BookId { get; set; }
    public Book? Book { get; set; }
}