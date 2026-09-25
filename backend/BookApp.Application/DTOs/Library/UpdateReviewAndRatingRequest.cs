using System.ComponentModel.DataAnnotations;

namespace BookApp.Application.DTOs.Library;

public class UpdateReviewAndRatingRequest : IValidatableObject
{
    [Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasında olmalıdır.")]
    public decimal? Rating { get; set; }

    [StringLength(2000, ErrorMessage = "Yorum en fazla 2000 karakter olabilir.")]
    public string? Review { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rating.HasValue && Rating.Value % 0.5m != 0)
        {
            yield return new ValidationResult("Puan 0.5'in katları olmalıdır (örn: 3.0, 4.5).", new[] { nameof(Rating) });
        }
    }
}
