
using System.ComponentModel.DataAnnotations;

namespace BookApp.Application.DTOs.Library
{
    public class AddRatingRequest
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasında olmalıdır.")]
        public decimal Rating { get; set; }
    }
}