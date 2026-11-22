
namespace BookApp.Application.Exceptions;

// 7.4-C.2 (R5): Eşzamanlı ekleme sırasında oluşan
// kitap zaten mevcut hatasını temsil eder.
public class UserBookAlreadyExistsException : InvalidOperationException
{
    public const string DefaultMessage =
        "Bu kitap zaten kitaplığınızda ekli veya arşivinizde. Arşivdeyse arşiv listenizden geri yükleyebilirsiniz.";

    public UserBookAlreadyExistsException() : base(DefaultMessage)
    {
    }

    public UserBookAlreadyExistsException(Exception innerException)
        : base(DefaultMessage, innerException)
    {
    }
}
