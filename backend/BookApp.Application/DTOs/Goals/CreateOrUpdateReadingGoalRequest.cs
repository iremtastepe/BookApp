namespace BookApp.Application.DTOs.Goals;

public class CreateOrUpdateReadingGoalRequest
{
    // Sınır kontrolü (1-1000) bir iş kuralıdır; tek kaynak ReadingGoalService.ValidateTargetBooks'tur.
    // Alan hiç gönderilmezse 0 olur ve servis tarafından 400 ile reddedilir.
    public int TargetBooks { get; set; }
}