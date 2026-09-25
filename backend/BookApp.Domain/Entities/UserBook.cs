using BookApp.Domain.Enums;

namespace BookApp.Domain.Entities;

public class UserBook
{
    public int Id { get; set; }

    // Bu UserBook kaydının hangi kullanıcıya ait olduğunu belirtir (foreign key)
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Bu UserBook kaydının ortak katalogdaki hangi Book'a bağlı olduğunu belirtir (foreign key)
    // Book'ta UserId yok çünkü Book ortak, kullanıcıya özel her şey burada (UserBook'ta) tutuluyor
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    // Kullanıcının bu kitapla ilgili okuma durumu (NotStarted, Reading, NextUp, Read, DidNotFinish)
    // Enum çünkü sabit, sonlu ve isimlendirilmiş kategoriler - Rating gibi sürekli bir değer değil
    public ReadingStatus Status { get; set; } = ReadingStatus.NotStarted;

    // Kullanıcı bu kitabı favorilerine eklemiş mi
    public bool IsFavorite { get; set; } = false;

    // Kullanıcının kitaba verdiği puan, 1-5 arası, 0.5 adımlarla (4.5 gibi) verilebilir
    // decimal çünkü yarım puan istiyoruz; int olsaydı sadece tam sayı (1,2,3,4,5) tutabilirdi
    // nullable çünkü kullanıcı henüz puan vermemiş olabilir (örneğin kitabı bitirmeden önce)
    public decimal? Rating { get; set; }

    // Kullanıcının kitabı bitirdikten sonra yazdığı yorum (tek, serbest metin)
    // Sayfa numarasına bağlı notlardan (Note entity) farklı - bu kitaba özel tek bir genel yorum
    // nullable çünkü kullanıcı yorum yazmamış olabilir
    public string? Review { get; set; }

    // Kullanıcının bu kitabı okumaya başladığı tarih, Status "Reading" olunca otomatik set ediliyor
    public DateTime? StartedAt { get; set; }

    // Kullanıcının bu kitabı bitirdiği tarih, Status "Read" olunca otomatik set ediliyor
    public DateTime? FinishedAt { get; set; }

    // Kullanıcının bu kitabı yarıda bıraktığı tarih, Status "DidNotFinish" olunca otomatik set ediliyor
    public DateTime? DidNotFinishAt { get; set; }

    // Bu kitabın kullanıcının kitaplığına eklendiği tarih
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Bu UserBook'a bağlı sayfa-numaralı notlar (şimdilik kullanılmıyor, ertelenen özellik)
    public ICollection<Note> Notes { get; set; } = new List<Note>();
}