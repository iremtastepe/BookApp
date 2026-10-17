using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Statistics;

// Bir okuma durumundan kaç kitap olduğunu söyler. Kitabı olmayan durumlar da 0 ile listelenir.
public class StatusCountDto
{
    public ReadingStatus Status { get; set; }

    // "Reading", "Read" gibi okunabilir ad. Frontend enum sayısını çözmek zorunda kalmasın diye ayrıca veriyoruz.
    public string StatusName { get; set; } = string.Empty;

    public int Count { get; set; }
}
