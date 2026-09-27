namespace BookApp.Application.DTOs.External;


/// Dış kitap API'lerinden (Google Books, Open Library vb.) gelen ham veriyi
/// ortak, standart bir şekle çeviren taşıyıcı model.
///
/// Bu sınıfın hiçbir iş mantığı yoktur (saf DTO) — amacı, Infrastructure
/// katmanındaki servislerin (GoogleBooksLookupService, OpenLibraryLookupService)
/// kendi ham JSON'larını Application katmanının anlayacağı ortak bir dile
/// çevirmesini sağlamaktır. Alanlar bilinçli olarak Book entity'sindeki
/// alanlarla birebir eşleşecek şekilde tasarlandı, çünkü BookService bu
/// DTO'yu doğrudan yeni bir Book'a dönüştürecek.

public class ExternalBookDto
{
    public required string Title { get; set; }

    public required string Author { get; set; }


    /// Tek bir ISBN alanı. Kaynak (Google Books, Open Library) hem ISBN-10
    /// hem ISBN-13 döndürüyorsa, ISBN-13 önceliklendirilip buraya yazılır;
    /// yalnızca ISBN-10 varsa ona düşülür. Barkod okutma akışıyla da tutarlı
    /// olsun diye (barkodlar EAN-13, yani ISBN-13 formatında okunur).

    public string? Isbn { get; set; }

    public string? CoverImageUrl { get; set; }

    public string? Description { get; set; }

    public int? PageCount { get; set; }

   
    /// Sadece yayın yılı. Kaynak API'ler genelde tam tarih ("2018-05-03")
    /// ya da sadece yıl ("2018") döndürebiliyor; hangi servis bu veriyi
    /// dolduruyorsa, tarih string'inden yıl kısmını ayıklayıp buraya
    /// sayısal olarak yazmakla yükümlüdür.

    public int? PublishedYear { get; set; }

    public string? Genre { get; set; }
}