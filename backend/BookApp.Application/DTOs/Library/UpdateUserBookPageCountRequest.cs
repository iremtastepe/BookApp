namespace BookApp.Application.DTOs.Library;

public class UpdateUserBookPageCountRequest
{
    // JsonRequired veya bool flag: alanın gövdede gönderilip gönderilmediğini ayırt etmek için
    public int? PageCount { get; set; }
}
