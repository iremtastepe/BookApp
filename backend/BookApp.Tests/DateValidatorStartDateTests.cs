using BookApp.Application.DTOs.Library;
using BookApp.Application.Helpers;
using Xunit;

namespace BookApp.Tests;

public class DateValidatorStartDateTests
{
    [Fact]
    public void ValidateStartDate_NullDate_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(null));
        Assert.Equal("Tarih bilgisi boş olamaz.", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_EmptyDate_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto()));
        Assert.Equal("Tarih bilgisi en az yıl içermelidir.", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_MissingYear_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto { Month = 5, Day = 10 }));
        Assert.Equal("Başlangıç tarihi için yıl zorunludur.", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_OnlyYear_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto { Year = 2023 }));
        Assert.Equal("Başlangıç tarihi tam tarih olmalıdır (yıl, ay ve gün zorunludur).", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_YearAndMonthWithoutDay_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto { Year = 2023, Month = 5 }));
        Assert.Equal("Başlangıç tarihi tam tarih olmalıdır (yıl, ay ve gün zorunludur).", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_YearBelow2000_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto { Year = 1999, Month = 12, Day = 31 }));
        Assert.Equal("Başlangıç yılı 2000 veya daha büyük olmalıdır.", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_FutureDateBeyondTolerance_ThrowsArgumentException()
    {
        var futureDate = DateTime.UtcNow.Date.AddDays(2);
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto
        {
            Year = futureDate.Year,
            Month = futureDate.Month,
            Day = futureDate.Day
        }));
        Assert.Equal("Gelecekteki bir tarih başlangıç tarihi olarak girilemez.", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_TodayAndTomorrow_ValidUnderTolerance()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // İkisi de hata fırlatmamalı
        DateValidator.ValidateStartDate(new CompletionDateDto { Year = today.Year, Month = today.Month, Day = today.Day });
        DateValidator.ValidateStartDate(new CompletionDateDto { Year = tomorrow.Year, Month = tomorrow.Month, Day = tomorrow.Day });
    }

    [Fact]
    public void ValidateStartDate_InvalidCalendarDay_ThrowsArgumentException()
    {
        // 31 Şubat geçersizdir
        var ex = Assert.Throws<ArgumentException>(() => DateValidator.ValidateStartDate(new CompletionDateDto { Year = 2023, Month = 2, Day = 31 }));
        Assert.Contains("28 değerinden büyük olamaz", ex.Message);
    }

    [Fact]
    public void ValidateStartDate_ValidPastDate_PassesSuccessfully()
    {
        DateValidator.ValidateStartDate(new CompletionDateDto { Year = 2021, Month = 6, Day = 15 });
    }

    [Fact]
    public void ToUtcCalendarDate_UnspecifiedKind_TreatedAsUtcWithoutShift()
    {
        var unspecified = new DateTime(2024, 5, 10, 1, 0, 0, DateTimeKind.Unspecified);
        var dateOnly = DateValidator.ToUtcCalendarDate(unspecified);

        Assert.Equal(new DateOnly(2024, 5, 10), dateOnly);
    }

    [Fact]
    public void ToUtcCalendarDate_UtcKind_PreservesDate()
    {
        var utc = new DateTime(2024, 5, 10, 23, 30, 0, DateTimeKind.Utc);
        var dateOnly = DateValidator.ToUtcCalendarDate(utc);

        Assert.Equal(new DateOnly(2024, 5, 10), dateOnly);
    }

    [Fact]
    public void IsStartedAfterFinished_YearOnlyFinishedDate_CorrectlyCompares()
    {
        var start2023 = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Başlangıç 2023, Bitiş 2022 -> Hata (true)
        Assert.True(DateValidator.IsStartedAfterFinished(start2023, 2022, null, null));

        // Başlangıç 2023, Bitiş 2023 -> Aynı yıl geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(start2023, 2023, null, null));

        // Başlangıç 2023, Bitiş 2024 -> Bitiş sonra geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(start2023, 2024, null, null));
    }

    [Fact]
    public void IsStartedAfterFinished_YearAndMonthFinishedDate_CorrectlyCompares()
    {
        var startMay2023 = new DateTime(2023, 5, 20, 0, 0, 0, DateTimeKind.Utc);

        // Bitiş Nisan 2023 -> Hata (true)
        Assert.True(DateValidator.IsStartedAfterFinished(startMay2023, 2023, 4, null));

        // Bitiş Mayıs 2023 -> Aynı ay geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(startMay2023, 2023, 5, null));

        // Bitiş Haziran 2023 -> Bitiş sonra geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(startMay2023, 2023, 6, null));
    }

    [Fact]
    public void IsStartedAfterFinished_FullFinishedDate_CorrectlyCompares()
    {
        var start = new DateTime(2023, 5, 15, 0, 0, 0, DateTimeKind.Utc);

        // Bitiş 14 Mayıs -> Hata (true)
        Assert.True(DateValidator.IsStartedAfterFinished(start, 2023, 5, 14));

        // Bitiş 15 Mayıs (aynı gün) -> Geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(start, 2023, 5, 15));

        // Bitiş 16 Mayıs -> Geçerli (false)
        Assert.False(DateValidator.IsStartedAfterFinished(start, 2023, 5, 16));
    }

    [Fact]
    public void IsStartedAfterFinished_FinishedYearNull_ReturnsFalse()
    {
        var start = new DateTime(2023, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(DateValidator.IsStartedAfterFinished(start, null, null, null));
    }

    [Fact]
    public void IsStartedAfterDidNotFinish_WithTolerance_CorrectlyEvaluates()
    {
        var dnf = new DateTime(2024, 4, 10, 0, 0, 0, DateTimeKind.Utc);

        // Başlangıç aynı gün (10 Nisan) -> Geçerli (false)
        Assert.False(DateValidator.IsStartedAfterDidNotFinish(new DateTime(2024, 4, 10, 0, 0, 0, DateTimeKind.Utc), dnf));

        // Başlangıç +1 gün tolerans içinde (11 Nisan) -> Geçerli (false)
        Assert.False(DateValidator.IsStartedAfterDidNotFinish(new DateTime(2024, 4, 11, 0, 0, 0, DateTimeKind.Utc), dnf));

        // Başlangıç +2 gün (12 Nisan) -> Hata (true)
        Assert.True(DateValidator.IsStartedAfterDidNotFinish(new DateTime(2024, 4, 12, 0, 0, 0, DateTimeKind.Utc), dnf));
    }

    [Fact]
    public void FormatPartialDate_FormatsTurkishProperly()
    {
        Assert.Equal("2024", DateValidator.FormatPartialDate(2024, null, null));
        Assert.Equal("Mayıs 2024", DateValidator.FormatPartialDate(2024, 5, null));
        Assert.Equal("15 Mayıs 2024", DateValidator.FormatPartialDate(2024, 5, 15));
        Assert.Equal("-", DateValidator.FormatPartialDate(null, null, null));
    }

    [Fact]
    public void FormatCalendarDate_FormatsTurkishProperly()
    {
        var dt = new DateTime(2024, 3, 8, 14, 30, 0, DateTimeKind.Utc);
        Assert.Equal("8 Mart 2024", DateValidator.FormatCalendarDate(dt));
    }
}
