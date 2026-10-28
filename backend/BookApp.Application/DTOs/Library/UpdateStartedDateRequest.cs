using System.Text.Json.Serialization;

namespace BookApp.Application.DTOs.Library;

/// <summary>
/// Kullanıcının kitabın başlangıç tarihini sonradan düzenlemesi için kullanılır.
/// </summary>
public class UpdateStartedDateRequest
{
    /// <summary>
    /// Başlangıç tarihi. Tam tarih (Yıl, Ay, Gün) zorunludur.
    /// JSON gövdesinde bu alan bulunmak zorundadır ([JsonRequired]).
    /// </summary>
    [JsonRequired]
    public CompletionDateDto? StartedDate { get; set; }
}
