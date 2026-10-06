namespace BookApp.Application.Helpers;

public static class IsbnHelper
{
    // Dis dunyaya acik tek metot: temizler, dogrular, ISBN-13 olarak dondurur.
    // Gecersiz girdide ArgumentException firlatir (controller bunu 400'e cevirecek).
    public static string Normalize(string input)
    {
        var cleaned = Clean(input);

        if (cleaned.Length == 10)
        {
            if (!IsValidIsbn10(cleaned))
                throw new ArgumentException("Gecersiz ISBN-10: karakterler veya kontrol hanesi hatali.");

            return ConvertIsbn10To13(cleaned);
        }

        if (cleaned.Length == 13)
        {
            if (!cleaned.All(char.IsAsciiDigit))
                throw new ArgumentException("Gecersiz ISBN-13: sadece rakam icermeli.");

            if (!cleaned.StartsWith("978") && !cleaned.StartsWith("979"))
                throw new ArgumentException("Bu barkod bir kitap ISBN'i degil (978 veya 979 ile baslamali).");

            var expected = CalculateIsbn13CheckDigit(cleaned[..12]);
            if (cleaned[12] != expected)
                throw new ArgumentException("Gecersiz ISBN-13: kontrol hanesi hatali.");

            return cleaned;
        }

        throw new ArgumentException("ISBN 10 veya 13 haneli olmali.");
    }

    // Tire ve bosluklari siler, kucuk x'i buyuk X yapar.
    private static string Clean(string input)
    {
        var chars = input.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray();
        return new string(chars).ToUpperInvariant();
    }

    // ISBN-10: ilk 9 hane rakam, son hane rakam veya X (=10).
    // Haneler 10,9,8...1 ile carpilip toplanir, toplam 11'e tam bolunmeli.
    private static bool IsValidIsbn10(string isbn)
    {
        var sum = 0;

        for (var i = 0; i < 10; i++)
        {
            int value;

            if (i == 9 && isbn[i] == 'X')
                value = 10;
            else if (char.IsAsciiDigit(isbn[i]))
                value = isbn[i] - '0';
            else
                return false;

            sum += value * (10 - i);
        }

        return sum % 11 == 0;
    }

    // Ilk 12 haneden 13. haneyi (kontrol hanesi) hesaplar.
    // Haneler sirayla 1 ve 3 ile carpilir; toplami 10'a tamamlayan sayi kontrol hanesidir.
    private static char CalculateIsbn13CheckDigit(string first12)
    {
        var sum = 0;

        for (var i = 0; i < 12; i++)
        {
            var digit = first12[i] - '0';
            sum += digit * (i % 2 == 0 ? 1 : 3);
        }

        var check = (10 - sum % 10) % 10;
        return (char)('0' + check);
    }

    // ISBN-10 -> ISBN-13: basina 978 ekle, eski kontrol hanesini at, yenisini hesapla.
    private static string ConvertIsbn10To13(string isbn10)
    {
        var first12 = "978" + isbn10[..9];
        return first12 + CalculateIsbn13CheckDigit(first12);
    }
}