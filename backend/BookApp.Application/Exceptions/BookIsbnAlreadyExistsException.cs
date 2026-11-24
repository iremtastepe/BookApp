namespace BookApp.Application.Exceptions;

// 7.4-C.5: Ortak katalogda ISBN benzersizlik ihlali.
public class BookIsbnAlreadyExistsException : InvalidOperationException
{
    public const string DefaultMessage =
        "Bu ISBN ile kayıtlı bir kitap zaten var.";

    public BookIsbnAlreadyExistsException() : base(DefaultMessage)
    {
    }

    public BookIsbnAlreadyExistsException(Exception innerException)
        : base(DefaultMessage, innerException)
    {
    }
}
