using System.Text.Json.Serialization;
using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Library;

public class UpdateUserBookStatusRequest
{
    // [JsonRequired]: "status" alanı JSON'da bulunmazsa istek reddedilir.
    // Bu olmadan {} gelince Status varsayılan değeri 0 (NotStarted) olur ve kitap sessizce sıfırlanırdı.
    [JsonRequired]
    public ReadingStatus Status { get; set; }

    // Status "Read" olduğunda: Seçenek A veya B için tarih bilgisi
    public CompletionDateDto? FinishedDate { get; set; }

    // Status "Read" olduğunda: Seçenek C (tarih hatırlamıyorum) için true
    public bool? FinishedDateUnknown { get; set; }
}