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
 
    // Kullanıcının bu kitapta şu an kaçıncı sayfada olduğu (isteğe bağlı ilerleme kaydı)
    // int? (nullable) çünkü sayfa takibi zorunlu değil ve iki farklı durumu ayırmamız gerekiyor:
    //   null -> kullanıcı ilerleme bilgisi hiç girmemiş (veya yarım bırakılan okunmuş kitapta bilinmiyor)
    //   0    -> kullanıcı ilerleme bilgisi girmiş ama henüz ilk sayfayı geçmemiş (ya da yeniden okumaya başlamış)
    // Durumla ilişkisi (7.4):
    //   - Bir durumu seçmek için sayfa girmek gerekmez; sayfa girmek de (otomatik NotStarted/NextUp -> Reading
    //     ve son sayfada otomatik Read dışında) durumu zorla değiştirmez.
    //   - Read'e geçişte kitabın toplam sayfa sayısı (PageCount) biliniyorsa CurrentPage = PageCount olur;
    //     PageCount bilinmiyorsa (null/0) CurrentPage mevcut değerini korur, sayı uydurulmaz.
    //   - Read'den çıkışta: Reading -> 0, NotStarted/NextUp -> 0, DidNotFinish -> null.
    //     Diğer tüm geçişlerde (Reading -> NextUp, DidNotFinish -> Reading vb.) gerçek ilerleme korunur.
    //   - Kitap Read durumundayken ve PageCount biliniyorsa ilerleme endpoint'i yalnızca PageCount'a eşit
    //     değeri kabul eder (küçük/büyük/null değer 400). PageCount bilinmiyorsa ek bir kısıt yoktur.
    public int? CurrentPage { get; set; }
 
    // Kullanıcının bu kitabı okumaya başladığı tarih. Kuralları:
    //   - NotStarted ve NextUp durumlarında her zaman null'dır: bu durumlara geçilince temizlenir
    //     (okunmamış/henüz başlanmamış kitabın başlangıç tarihi olmaz).
    //   - NotStarted/NextUp -> Reading geçişinde boşsa o an atanır.
    //   - Read veya DidNotFinish -> Reading geçişinde boşsa boş kalır (sistem bilinmeyen bir
    //     başlangıç tarihi uydurmaz), doluysa korunur.
    //   - Reading -> Reading tekrarında değişmez.
    //   - Read ve DidNotFinish durumlarına geçilirken dokunulmaz (geçmiş başlangıç bilgisi korunur).
    //   - Kullanıcı Reading, Read ve DidNotFinish durumlarında PUT /api/Library/{id}/started-date
    //     ile tam tarih olarak düzenleyebilir.
    public DateTime? StartedAt { get; set; }
 
    // Kullanıcının bu kitabı bitirdiği takvim yılı, ayı ve günü (Single Source of Truth).
    // Yalnızca Status "Read" olan kitaplarda anlamlıdır. Kısmi tarihleri destekler:
    //   - Yalnızca yıl: FinishedYear dolu, diğerleri null
    //   - Ay ve yıl: FinishedYear ve FinishedMonth dolu, FinishedDay null
    //   - Tam tarih: üçü de dolu
    //   - Tarih bilinmiyor: üçü de null
    public int? FinishedYear { get; set; }
    public int? FinishedMonth { get; set; }
    public int? FinishedDay { get; set; }
 
    // Kullanıcının bu kitabı yarıda bıraktığı tarih. Status "DidNotFinish" olunca boşsa set edilir;
    // Status "DidNotFinish" dışına çıkınca temizlenir
    public DateTime? DidNotFinishAt { get; set; }
    // Bu kitabın kullanıcının kitaplığına eklendiği tarih
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
 
    // Bu UserBook'a bağlı sayfa-numaralı notlar (şimdilik kullanılmıyor, ertelenen özellik)
    public ICollection<Note> Notes { get; set; } = new List<Note>();
}