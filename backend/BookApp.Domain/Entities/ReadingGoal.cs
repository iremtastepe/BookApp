namespace BookApp.Domain.Entities;

public class ReadingGoal
{
    public int Id { get; set; }

    // Bu hedefin hangi kullanıcıya ait olduğunu belirtir (Foreign Key)
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Hangi takvim yılı için hedef belirlendiği (örn. 2026)
    public int Year { get; set; }

    // Kullanıcının o yıl okumayı hedeflediği kitap sayısı (> 0 olmalı)
    public int TargetBooks { get; set; }

    // Hedefin ilk oluşturulma tarihi
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Hedef güncellendiğinde kaydedilecek tarih
    public DateTime? UpdatedAt { get; set; }
}
