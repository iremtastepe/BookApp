using System.Text.Json.Serialization;
using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Library;

public class UpdateUserBookStatusRequest
{
    // [JsonRequired]: "status" alanı JSON'da bulunmazsa istek reddedilir.
    // Bu olmadan {} gelince Status varsayılan değeri 0 (NotStarted) olur ve kitap sessizce sıfırlanırdı.
    [JsonRequired]
    public ReadingStatus Status { get; set; }
}