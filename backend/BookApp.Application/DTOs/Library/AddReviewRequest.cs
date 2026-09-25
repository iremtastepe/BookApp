
using System.ComponentModel.DataAnnotations;

namespace BookApp.Application.DTOs.Library
{
    public class AddReviewRequest
    {
        [Required]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Yorum 1-2000 karakter arasında olmalıdır.")]
        public string Review { get; set; } = string.Empty;
    }
}