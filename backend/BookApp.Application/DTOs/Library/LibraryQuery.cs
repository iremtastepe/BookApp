using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Library;

// GET /api/Library'e gelen tüm arama/filtre/sıralama/sayfalama parametreleri
public class LibraryQuery
{
    // Kitap adı veya yazarda arar (büyük/küçük harf duyarsız)
    public string? Search { get; set; }

    // Boşsa durum filtresi uygulanmaz; doluysa "bunlardan herhangi biri" mantığıyla çalışır.
    // Örnek: ?status=NotStarted&status=NextUp
    public List<ReadingStatus> Status { get; set; } = new();

    // null: filtre yok, true: sadece favoriler, false: sadece favori olmayanlar
    public bool? IsFavorite { get; set; }

    public LibrarySortBy SortBy { get; set; } = LibrarySortBy.AddedAt;
    public LibrarySortDirection SortDir { get; set; } = LibrarySortDirection.Desc;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// Sıralama seçeneklerini string yerine enum yaptım: yazım hatası (örn. "titel") model binding
// aşamasında otomatik 400'e döner, kodda elle string karşılaştırması yazmamız gerekmez.
public enum LibrarySortBy
{
    AddedAt,
    Title,
    Rating
}

public enum LibrarySortDirection
{
    Asc,
    Desc
}