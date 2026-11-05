namespace BookApp.Application.Exceptions;
 
// 7.1: Arşivdeki bir kitaplık kaydı üzerinde yazma işlemi denendiğinde fırlatılır.
// Bilerek InvalidOperationException'dan TÜRETİLMEZ: controller'daki mevcut "catch (InvalidOperationException) -> 400"
// blokları bu hatayı yanlışlıkla 400'e çevirmesin; Adım 5'te ayrı bir catch ile 409'a eşlenecek.
public class ArchivedUserBookException : Exception
{
    public ArchivedUserBookException(string message)
        : base(message)
    {
    }
}