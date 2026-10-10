using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;

namespace BookApp.Application.Services;

public class UserBookService : IUserBookService
{
    private readonly IUserBookRepository _userBookRepository;
    private readonly IBookRepository _bookRepository;

    public UserBookService(IUserBookRepository userBookRepository, IBookRepository bookRepository)
    {
        _userBookRepository = userBookRepository;
        _bookRepository = bookRepository;
    }

    public async Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request)
    {
        var book = await _bookRepository.GetByIdAsync(request.BookId);
        if (book is null)
            throw new KeyNotFoundException("Kitap katalogda bulunamadı.");        // 404

        if (await _userBookRepository.ExistsAsync(userId, request.BookId))
            throw new InvalidOperationException("Bu kitap zaten kitaplığında.");  // 409

        var userBook = new UserBook
        {
            UserId = userId,
            BookId = request.BookId,
            Status = ReadingStatus.NotStarted,
            IsFavorite = false,
            AddedAt = DateTime.UtcNow,
            Book = book
        };

        await _userBookRepository.AddAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<PagedResponse<UserBookDto>> GetLibraryAsync(int userId, LibraryQuery query)
    {
        // Sayfalama kuralları: geçersiz değer sessizce düzeltilmez, kullanıcıya 400 olarak bildirilir.
        // Bu kontroller veritabanına gitmeden önce yapılır: geçersiz istek için boşuna sorgu atmayız.
        if (query.Page < 1)
            throw new ArgumentException("page 1 veya daha büyük olmalıdır.");  // 400

        if (query.PageSize < 1 || query.PageSize > 50)
            throw new ArgumentException("pageSize 1 ile 50 arasında olmalıdır.");  // 400

        // Model binding "?status=99" gibi sayıları enum'da tanımlı olmasa bile kabul eder,
        // bu yüzden her değerin gerçekten ReadingStatus'ün üyesi olduğunu burada doğrularız.
        foreach (var status in query.Status)
        {
            if (!Enum.IsDefined(status))
            {
                var validValues = string.Join(", ", Enum.GetValues<ReadingStatus>().Select(s => $"{(int)s} ({s})"));
                throw new ArgumentException($"Geçersiz okuma durumu. Geçerli değerler: {validValues}.");  // 400
            }
        }

        // Filtreleme, sıralama ve sayfalama veritabanında yapılır (repository); servis sadece sonucu DTO'ya çevirir
        var (items, total) = await _userBookRepository.GetPagedByUserIdAsync(userId, query);

        return new PagedResponse<UserBookDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<UserBookDto> UpdateStatusAsync(int userId, int userBookId, UpdateUserBookStatusRequest request)
    {
        // JSON'dan gelen bir sayı (örn. 99), enum'da tanımlı olmasa bile hata vermeden Status'a aktarılabilir.
        // Bu yüzden değerin gerçekten ReadingStatus'ün üyelerinden biri olduğunu burada doğrularız.
        // Bu kontrol veritabanına gitmeden önce yapılır: geçersiz istek için boşuna sorgu atmayız.
        if (!Enum.IsDefined(request.Status))
        {
            // Geçerli değerleri enum'dan türetiyoruz, böylece enum'a yeni üye eklenirse mesaj bayatlamaz
            var validValues = string.Join(", ", Enum.GetValues<ReadingStatus>().Select(s => $"{(int)s} ({s})"));
            throw new ArgumentException($"Geçersiz okuma durumu. Geçerli değerler: {validValues}.");  // 400
        }

        // Kayıt hem id hem userId ile aranır: başkasının kaydı bulunamaz, dışarıdan "yok" ile aynı görünür
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");  // 404

        // Durum ve tarih kuralları tek bir yerde (ApplyStatusChange) tutulur
        ApplyStatusChange(userBook, request.Status);

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateProgressAsync(int userId, int userBookId, UpdateProgressRequest request)
    {
        // Kayıt hem id hem userId ile aranır: başkasının kaydı bulunamaz, dışarıdan "yok" ile aynı görünür
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");  // 404

        // null gönderilirse kayıtlı ilerleme temizlenir; okuma durumu değişmez
        if (request.CurrentPage is null)
        {
            userBook.CurrentPage = null;
            await _userBookRepository.UpdateAsync(userBook);
            return MapToDto(userBook);
        }

        var newPage = request.CurrentPage.Value;

        if (newPage < 0)
            throw new ArgumentException("Sayfa numarası negatif olamaz.");  // 400

        // PageCount null veya 0 ise toplam sayfa bilinmiyor/geçersiz sayılır:
        // üst sınır kontrolü yapılamaz, ilerleme yine de girilebilir
        var totalPages = userBook.Book.PageCount;
        var isTotalPagesKnown = totalPages is > 0;

        if (isTotalPagesKnown && newPage > totalPages)
            throw new ArgumentException($"Sayfa numarası kitabın toplam sayfa sayısını ({totalPages}) aşamaz.");  // 400

        // Eski değer, yenisini kaydetmeden ÖNCE alınmalı; ilerleme gerçekten arttı mı diye karşılaştıracağız.
        // CurrentPage null ise (hiç girilmemiş) 0 kabul edilir
        var previousPage = userBook.CurrentPage ?? 0;
        userBook.CurrentPage = newPage;

        // Otomatik durum geçişleri SADECE sayfa gerçekten ilerlediyse yapılır:
        // aynı sayfayı tekrar kaydetmek veya sayfayı geri çekmek durumu değiştirmez.
        // (Kullanıcının manuel seçtiği durum bu yüzden otomatik olarak ezilmez.)
        if (newPage > previousPage)
        {
            // Henüz okunmaya başlanmamış kitapta 0'dan büyük sayfaya ilerlemek, okumanın başladığı anlamına gelir.
            // DidNotFinish ve Read bu koşula girmez: onlar otomatik olarak değişmez
            if (userBook.Status is ReadingStatus.NotStarted or ReadingStatus.NextUp)
                ApplyStatusChange(userBook, ReadingStatus.Reading);

            // Toplam sayfa biliniyorsa ve son sayfaya ulaşıldıysa kitap bitmiştir.
            // Toplam sayfa bilinmiyorsa son sayfanın hangisi olduğunu bilemeyiz, otomatik Read yapılmaz
            if (userBook.Status == ReadingStatus.Reading && isTotalPagesKnown && newPage == totalPages)
                ApplyStatusChange(userBook, ReadingStatus.Read);
        }

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        if (userBook.Status != ReadingStatus.Read && userBook.Status != ReadingStatus.DidNotFinish)
            throw new InvalidOperationException("Bir kitaba yorum yapmak veya puan vermek için kitabı bitirmiş veya okumayı bırakmış olmalısınız.");

        userBook.Rating = request.Rating;
        userBook.Review = request.Review;

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> SetFavoriteAsync(int userId, int userBookId, SetFavoriteRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");  // 404

        // Sadece okunmuş kitaplar favoriye eklenebilir (favoriden çıkarmak her zaman serbest)
        if (request.IsFavorite && userBook.Status != ReadingStatus.Read)
            throw new InvalidOperationException("Sadece okuduğun kitapları favorilere ekleyebilirsin.");  // 409

        userBook.IsFavorite = request.IsFavorite;

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    // Okuma durumunun değişmesiyle ilgili TÜM kurallar burada toplanır.
    // Hem kullanıcının manuel durum değişikliği hem de sayfa ilerlemesinden
    // tetiklenen otomatik geçişler bu metodu kullanır; böylece iki yol birbiriyle çelişemez.
    private static void ApplyStatusChange(UserBook userBook, ReadingStatus newStatus)
    {
        userBook.Status = newStatus;

        // Bitirme tarihi ve favori sadece "Read" durumuna aittir: kitap Read değilse ikisi de temizlenir
        if (newStatus != ReadingStatus.Read)
        {
            userBook.FinishedAt = null;
            userBook.IsFavorite = false;
        }

        // Yarım bırakma tarihi sadece "DidNotFinish" durumuna aittir
        if (newStatus != ReadingStatus.DidNotFinish)
            userBook.DidNotFinishAt = null;

        // StartedAt hiçbir durumda silinmez, geçmiş başlangıç bilgisi olarak kalır.
        // "??=" -> "null ise ata, doluysa dokunma": tarihler gereksiz yere yeniden yazılmaz
        var now = DateTime.UtcNow;
        switch (newStatus)
        {
            case ReadingStatus.Reading:
                userBook.StartedAt ??= now;
                break;
            case ReadingStatus.Read:
                userBook.FinishedAt ??= now;
                break;
            case ReadingStatus.DidNotFinish:
                userBook.DidNotFinishAt ??= now;
                break;
        }
    }

    private static UserBookDto MapToDto(UserBook userBook) => new()
    {
        Id = userBook.Id,
        BookId = userBook.BookId,
        Title = userBook.Book.Title,
        Author = userBook.Book.Author,
        CoverImageUrl = userBook.Book.CoverImageUrl,
        Status = userBook.Status,
        IsFavorite = userBook.IsFavorite,
        Rating = userBook.Rating,
        Review = userBook.Review,
        CurrentPage = userBook.CurrentPage,
        PageCount = userBook.Book.PageCount,
        ProgressPercentage = CalculateProgressPercentage(userBook),
        StartedAt = userBook.StartedAt,
        FinishedAt = userBook.FinishedAt,
        DidNotFinishAt = userBook.DidNotFinishAt,
        AddedAt = userBook.AddedAt
    };

    // İlerleme yüzdesi veritabanında saklanmaz, her seferinde hesaplanır:
    // PageCount sonradan düzeltilirse saklanan bir yüzde bayat kalırdı
    private static double? CalculateProgressPercentage(UserBook userBook)
    {
        // Kullanıcı kitabı "Okundu" olarak işaretlediyse, sayfa bilgisi olsun olmasın tamamlanmış kabul edilir
        if (userBook.Status == ReadingStatus.Read)
            return 100;

        // Toplam sayfa null veya 0 ise bilinmiyor/geçersiz sayılır; CurrentPage null ise ilerleme girilmemiştir
        var pageCount = userBook.Book.PageCount;
        if (pageCount is null or <= 0 || userBook.CurrentPage is null)
            return null;

        var percentage = (double)userBook.CurrentPage.Value / pageCount.Value * 100;

        // Ortak katalogdaki PageCount sonradan düşürülürse CurrentPage onu aşabilir, yüzde 100'ü geçmesin
        return Math.Round(Math.Min(percentage, 100), 1);
    }
}