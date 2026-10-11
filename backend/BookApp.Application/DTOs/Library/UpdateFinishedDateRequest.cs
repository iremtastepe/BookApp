namespace BookApp.Application.DTOs.Library;

public class UpdateFinishedDateRequest
{
    // Kullanıcı tam veya kısmi tarih girmek isterse doldurur
    public CompletionDateDto? FinishedDate { get; set; }

    // Kullanıcı okuma tarihini bilmiyor/hatırlamıyor olarak ayarlamak isterse true gönderir
    public bool? FinishedDateUnknown { get; set; }
}
