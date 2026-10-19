using BookApp.Application.DTOs.Library;

namespace BookApp.Application.Helpers;

/// <summary>
/// Tamamlanma tarihlerinin geçerliliğini ve takvim kurallarını denetleyen yardımcı sınıf.
/// Ayrıca yıl/ay aralığı kuralını (hedefler, istatistikler, bitirme tarihi) tek yerde tutar.
/// </summary>
public static class DateValidator
{
    // Uygulamada kabul edilen en küçük yıl (hedef, istatistik ve bitirme tarihi için ortak)
    public const int MinYear = 2000;

    // Kabul edilen en büyük yıl: içinde bulunulan yıl + 1 (gelecek yıl için hedef planlanabilir)
    public static int MaxYear => DateTime.UtcNow.Year + 1;

    /// <summary>
    /// Yıl, MinYear ile MaxYear (dahil) arasında olmalıdır. Aksi halde ArgumentException (400) fırlatır.
    /// Hedefler (PUT/GET) ve istatistik sorguları bu metodu kullanır.
    /// </summary>
    public static void ValidateYearRange(int year)
    {
        if (year < MinYear || year > MaxYear)
            throw new ArgumentException($"Yıl {MinYear} ile {MaxYear} arasında olmalıdır.");
    }

    /// <summary>
    /// Ay 1 ile 12 arasında olmalıdır. Aksi halde ArgumentException (400) fırlatır.
    /// </summary>
    public static void ValidateMonth(int month)
    {
        if (month < 1 || month > 12)
            throw new ArgumentException("Ay 1 ile 12 arasında olmalıdır.");
    }

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

        // Alt sınır: hedef ve istatistiklerle aynı kural (MinYear)
        if (year < MinYear)
            throw new ArgumentException($"Yıl {MinYear} veya daha büyük olmalıdır.");

        // Teknik güvenlik: 9999'dan büyük yıllar DateTime ile hesaplanamaz; zaten gelecek tarihtir
        if (year > 9999)
            throw new ArgumentException("Gelecekteki bir yıl tamamlanma yılı olarak girilemez.");

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
        ValidateMonth(month);

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