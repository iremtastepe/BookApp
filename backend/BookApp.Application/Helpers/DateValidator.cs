using BookApp.Application.DTOs.Library;

namespace BookApp.Application.Helpers;

/// <summary>
/// Tamamlanma tarihlerinin geçerliliğini ve takvim kurallarını denetleyen yardımcı sınıf.
/// </summary>
public static class DateValidator
{
    /// <summary>
    /// Gelen tarih nesnesini (tam tarih, ay-yıl veya sadece yıl) doğrular.
    /// Hata varsa ArgumentException fırlatır.
    /// </summary>
    public static void ValidateCompletionDate(CompletionDateDto? date)
    {
        if (date is null)
            throw new ArgumentException("Tarih bilgisi boş olamaz.");

        var hasYear = date.Year.HasValue;
        var hasMonth = date.Month.HasValue;
        var hasDay = date.Day.HasValue;

        // Hiçbir alan gönderilmemişse
        if (!hasYear && !hasMonth && !hasDay)
            throw new ArgumentException("Tarih bilgisi en az yıl içermelidir.");

        // Hiyerarşi kontrolleri:
        // Gün gönderildiyse ay ve yıl zorunludur
        if (hasDay && (!hasMonth || !hasYear))
            throw new ArgumentException("Gün belirtildiğinde ay ve yıl da belirtilmelidir.");

        // Ay gönderildiyse yıl zorunludur
        if (hasMonth && !hasYear)
            throw new ArgumentException("Ay belirtildiğinde yıl da belirtilmelidir.");

        var year = date.Year!.Value;

        // Yıl geçerlilik aralığı (1 - 9999)
        if (year < 1 || year > 9999)
            throw new ArgumentException("Yıl 1 ile 9999 arasında olmalıdır.");

        // Yerel saat dilimi toleransı: Sunucu UTC takvim tarihinden en fazla 1 gün ilerisi kabul edilir
        var maxCalendarDate = DateTime.UtcNow.Date.AddDays(1);
        var maxYear = maxCalendarDate.Year;
        var maxMonth = maxCalendarDate.Month;

        // 1. Sadece Yıl kontrolü
        if (!hasMonth && !hasDay)
        {
            if (year > maxYear)
                throw new ArgumentException($"Gelecekteki bir yıl ({year}) tamamlanma yılı olarak girilemez.");

            return;
        }

        // 2. Yıl ve Ay kontrolleri
        var month = date.Month!.Value;
        if (month < 1 || month > 12)
            throw new ArgumentException("Ay 1 ile 12 arasında olmalıdır.");

        if (!hasDay)
        {
            // Sadece Yıl + Ay: Gelecek ay kontrolü (1 gün tolerans ayı veya yılı aşamaz)
            if (year > maxYear || (year == maxYear && month > maxMonth))
                throw new ArgumentException("Gelecekteki bir ay/yıl tamamlanma tarihi olarak girilemez.");

            return;
        }

        // 3. Tam Tarih (Yıl + Ay + Gün) kontrolleri
        var day = date.Day!.Value;
        if (day < 1 || day > 31)
            throw new ArgumentException("Gün 1 ile 31 arasında olmalıdır.");

        var daysInMonth = DateTime.DaysInMonth(year, month);
        if (day > daysInMonth)
            throw new ArgumentException($"{year} yılı {month}. ayı için gün {daysInMonth} değerinden büyük olamaz.");

        var completionDate = new DateTime(year, month, day);
        if (completionDate > maxCalendarDate)
            throw new ArgumentException("Gelecekteki bir tarih tamamlanma tarihi olarak girilemez.");
    }
}
