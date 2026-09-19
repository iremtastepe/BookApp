namespace BookApp.Application.DTOs.Books;

public class BookResponse
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
}