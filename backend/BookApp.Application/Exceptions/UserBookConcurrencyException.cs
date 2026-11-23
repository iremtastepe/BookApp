namespace BookApp.Application.Exceptions;

// 7.4-C.3 (R4): Eşzamanlı güncelleme sırasında oluşan
// veri çakışması hatasını temsil eder.
public class UserBookConcurrencyException : InvalidOperationException
{
    public const string DefaultMessage =
        "Bu kitap kaydı başka bir işlem tarafından güncellendi. Lütfen sayfayı yenileyip tekrar deneyin.";

    public UserBookConcurrencyException() : base(DefaultMessage)
    {
    }

    public UserBookConcurrencyException(Exception innerException)
        : base(DefaultMessage, innerException)
    {
    }
}
