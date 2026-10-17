namespace BookApp.Application.DTOs.Statistics;

// "Bu isimden (yazar veya tür) şu kadar okunan kitap var" bilgisini taşır.
// Hem yazar hem tür listelerinde aynı şekil kullanıldığı için tek sınıf yeterli.
public class NameCountDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
