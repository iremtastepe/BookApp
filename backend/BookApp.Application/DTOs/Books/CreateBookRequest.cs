using System.ComponentModel.DataAnnotations;

namespace BookApp.Application.DTOs.Books;

public class CreateBookRequest
{
    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Isbn { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    public string? Description { get; set; }

    [Range(1, 10000)]
    public int? PageCount { get; set; }

    [Range(1, 2100)]
    public int? PublishedYear { get; set; }

    [MaxLength(100)]
    public string? Genre { get; set; }
}