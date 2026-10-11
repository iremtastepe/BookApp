using System.ComponentModel.DataAnnotations;

namespace BookApp.Application.DTOs.Goals;

public class CreateOrUpdateReadingGoalRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Hedef kitap sayısı 1 veya daha büyük olmalıdır.")]
    public int TargetBooks { get; set; }
}
