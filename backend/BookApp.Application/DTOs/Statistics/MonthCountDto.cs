using System.Collections.Generic;

namespace BookApp.Application.DTOs.Statistics;

// Aylık dağılımın bir satırı: "3. ayda 4 kitap tamamlandı" (Month: 1-12)
public class MonthCountDto
{
    public int Month { get; set; }
    public int Count { get; set; }
}
