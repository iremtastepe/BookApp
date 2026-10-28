using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Helpers;
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
            throw new KeyNotFoundException("Kitap bulunamadı.");

        var alreadyExists = await _userBookRepository.ExistsAsync(userId, request.BookId);
        if (alreadyExists)
            throw new InvalidOperationException("Bu kitap zaten kütüphanenizde ekli.");

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
        if (query.Page < 1)
            throw new ArgumentException("page 1 veya daha büyük olmalıdır.");

        if (query.PageSize < 1 || query.PageSize > 50)
            throw new ArgumentException("pageSize 1 ile 50 arasında olmalıdır.");

        foreach (var status in query.Status)
        {
            if (!Enum.IsDefined(status))
            {
                var validValues = string.Join(", ", Enum.GetValues<ReadingStatus>().Select(s => $"{(int)s} ({s})"));
                throw new ArgumentException($"Geçersiz okuma durumu. Geçerli değerler: {validValues}.");
            }
        }

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
        if (!Enum.IsDefined(request.Status))
        {
            var validValues = string.Join(", ", Enum.GetValues<ReadingStatus>().Select(s => $"{(int)s} ({s})"));
            throw new ArgumentException($"Geçersiz okuma durumu. Geçerli değerler: {validValues}.");
        }

        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        // Read dışındaki bir duruma geçiliyorsa tarih parametresi kabul edilmez
        if (request.Status != ReadingStatus.Read)
        {
            if (request.FinishedDate is not null || request.FinishedDateUnknown == true)
                throw new ArgumentException("Bitiş tarihi yalnızca 'Okundu' (Read) durumu için belirtilebilir.");

            ApplyStatusChange(userBook, request.Status);
        }
        else
        {
            // Çelişen tarih parametreleri kontrolü: her durumda (zaten Read olsa bile) reddedilir
            var hasFinishedDate = request.FinishedDate is not null;
            var isUnknown = request.FinishedDateUnknown == true;

            if (hasFinishedDate && isUnknown)
                throw new ArgumentException("finishedDate ve finishedDateUnknown aynı anda belirtilemez.");

            // Kitap zaten Read durumundaysa: durumun tekrar Read gönderilmesi mevcut bitiş tarihini DEĞİŞTİRMEZ veya TEMİZLEMEZ.
            if (userBook.Status == ReadingStatus.Read)
            {
                // Mevcut tarih korunur, hiçbir alan ezilmez
            }
            else
            {
                // Kitap ilk kez Read durumuna geçiriliyorsa: seçeneklerden tam olarak biri zorunludur
                if (!hasFinishedDate && !isUnknown)
                    throw new ArgumentException("Okundu durumuna geçerken bitiş tarihi (finishedDate) veya bilinmiyor seçeneği (finishedDateUnknown: true) belirtilmelidir.");

                int? targetYear = null;
                int? targetMonth = null;
                int? targetDay = null;

                if (!isUnknown)
                {
                    // Validation-first: Tarih nesnesini doğrula
                    DateValidator.ValidateCompletionDate(request.FinishedDate);

                    targetYear = request.FinishedDate!.Year;
                    targetMonth = request.FinishedDate.Month;
                    targetDay = request.FinishedDate.Day;

                    // Başlangıç tarihi varsa çelişki kontrolü yap
                    if (userBook.StartedAt.HasValue)
                    {
                        if (DateValidator.IsStartedAfterFinished(userBook.StartedAt.Value, targetYear, targetMonth, targetDay))
                        {
                            var startedStr = DateValidator.FormatCalendarDate(userBook.StartedAt.Value);
                            var finStr = DateValidator.FormatPartialDate(targetYear, targetMonth, targetDay);
                            throw new ArgumentException($"Bitirme tarihi ({finStr}), kitabın başlangıç tarihinden ({startedStr}) önce olamaz.");
                        }
                    }
                }

                // Tüm doğrulamalar başarılı: Mutation-last
                ApplyStatusChange(userBook, ReadingStatus.Read);
                userBook.FinishedYear = targetYear;
                userBook.FinishedMonth = targetMonth;
                userBook.FinishedDay = targetDay;
            }
        }

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateProgressAsync(int userId, int userBookId, UpdateProgressRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        // null gönderilirse kayıtlı ilerleme temizlenir; okuma durumu değişmez
        if (request.CurrentPage is null)
        {
            userBook.CurrentPage = null;
            await _userBookRepository.UpdateAsync(userBook);
            return MapToDto(userBook);
        }

        var newPage = request.CurrentPage.Value;

        if (newPage < 0)
            throw new ArgumentException("Sayfa numarası negatif olamaz.");

        var totalPages = userBook.Book.PageCount;
        var isTotalPagesKnown = totalPages is > 0;

        if (isTotalPagesKnown && newPage > totalPages)
            throw new ArgumentException($"Sayfa numarası kitabın toplam sayfa sayısını ({totalPages}) aşamaz.");

        var previousPage = userBook.CurrentPage ?? 0;

        // Otomatik durum geçişleri SADECE sayfa gerçekten ilerlediyse yapılır
        if (newPage > previousPage)
        {
            var isReachingEnd = isTotalPagesKnown && newPage == totalPages;

            if (isReachingEnd && userBook.Status != ReadingStatus.Read)
            {
                // Validation-first: LocalDate veya UTC bitirme tarihi hesaplama ve kontrolü
                int? finYear;
                int? finMonth;
                int? finDay;

                if (request.LocalDate is not null)
                {
                    DateValidator.ValidateCompletionDate(request.LocalDate);
                    finYear = request.LocalDate.Year;
                    finMonth = request.LocalDate.Month;
                    finDay = request.LocalDate.Day;
                }
                else
                {
                    var utcNow = DateTime.UtcNow;
                    finYear = utcNow.Year;
                    finMonth = utcNow.Month;
                    finDay = utcNow.Day;

                    // Senaryo C: StartedAt bugünden tam 1 gün ilerideyse UTC toleransı gereği bitirme günü StartedAt gününe hizalanır
                    if (userBook.StartedAt.HasValue)
                    {
                        var startDate = DateValidator.ToUtcCalendarDate(userBook.StartedAt.Value);
                        var todayUtc = DateOnly.FromDateTime(utcNow);
                        if (startDate == todayUtc.AddDays(1))
                        {
                            finYear = startDate.Year;
                            finMonth = startDate.Month;
                            finDay = startDate.Day;
                        }
                    }
                }

                // Başlangıç tarihi varsa tutarlılık denetimi
                if (userBook.StartedAt.HasValue)
                {
                    if (DateValidator.IsStartedAfterFinished(userBook.StartedAt.Value, finYear, finMonth, finDay))
                    {
                        var startedStr = DateValidator.FormatCalendarDate(userBook.StartedAt.Value);
                        var finStr = DateValidator.FormatPartialDate(finYear, finMonth, finDay);
                        throw new ArgumentException($"Bitirme tarihi ({finStr}), kitabın başlangıç tarihinden ({startedStr}) önce olamaz.");
                    }
                }

                // Doğrudan Read geçişi: NotStarted/NextUp durumundan tek işlemde son sayfaya gelindiğinde,
                // ara Reading geçişi yapılmadan doğrudan Read uygulanır. Böylece StartedAt uydurulmaz (null ise null kalır).
                ApplyStatusChange(userBook, ReadingStatus.Read);
                userBook.CurrentPage = newPage;
                userBook.FinishedYear = finYear;
                userBook.FinishedMonth = finMonth;
                userBook.FinishedDay = finDay;
            }
            else
            {
                // Kitap henüz bitmediyse veya zaten Read ise standart ilerleme
                userBook.CurrentPage = newPage;

                if (userBook.Status is ReadingStatus.NotStarted or ReadingStatus.NextUp)
                    ApplyStatusChange(userBook, ReadingStatus.Reading);
            }
        }
        else
        {
            userBook.CurrentPage = newPage;
        }

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateFinishedDateAsync(int userId, int userBookId, UpdateFinishedDateRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        if (userBook.Status != ReadingStatus.Read)
            throw new InvalidOperationException("Yalnızca 'Okundu' (Read) durumundaki kitapların bitiş tarihi güncellenebilir.");

        var hasFinishedDate = request.FinishedDate is not null;
        var isUnknown = request.FinishedDateUnknown == true;

        if (hasFinishedDate && isUnknown)
            throw new ArgumentException("finishedDate ve finishedDateUnknown aynı anda belirtilemez.");

        if (!hasFinishedDate && !isUnknown)
            throw new ArgumentException("Bitiş tarihi (finishedDate) veya bilinmiyor seçeneği (finishedDateUnknown: true) belirtilmelidir.");

        int? targetYear = null;
        int? targetMonth = null;
        int? targetDay = null;

        if (!isUnknown)
        {
            // Validation-first
            DateValidator.ValidateCompletionDate(request.FinishedDate);

            targetYear = request.FinishedDate!.Year;
            targetMonth = request.FinishedDate.Month;
            targetDay = request.FinishedDate.Day;

            if (userBook.StartedAt.HasValue)
            {
                if (DateValidator.IsStartedAfterFinished(userBook.StartedAt.Value, targetYear, targetMonth, targetDay))
                {
                    var startedStr = DateValidator.FormatCalendarDate(userBook.StartedAt.Value);
                    var finStr = DateValidator.FormatPartialDate(targetYear, targetMonth, targetDay);
                    throw new ArgumentException($"Bitirme tarihi ({finStr}), kitabın başlangıç tarihinden ({startedStr}) önce olamaz.");
                }
            }
        }

        // Mutation-last
        userBook.FinishedYear = targetYear;
        userBook.FinishedMonth = targetMonth;
        userBook.FinishedDay = targetDay;

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateStartedDateAsync(int userId, int userBookId, UpdateStartedDateRequest request)
    {
        // 1. Validation-first: Tarih formatı ve geçerliliği
        DateValidator.ValidateStartDate(request.StartedDate);

        // 2. Kayıt arama: kullanıcıya ait olmayan kayıtlar 404
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        // 3. Durum kontrolü: yalnızca Reading, Read veya DidNotFinish durumunda başlangıç tarihi girilebilir
        if (userBook.Status is ReadingStatus.NotStarted or ReadingStatus.NextUp)
            throw new InvalidOperationException("Yalnızca 'Okunuyor', 'Okundu' veya 'Yarım bırakıldı' durumundaki kitaplar için başlangıç tarihi düzenlenebilir.");

        var year = request.StartedDate!.Year!.Value;
        var month = request.StartedDate.Month!.Value;
        var day = request.StartedDate.Day!.Value;

        var targetStartDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // 4. Tutarlılık kontrolleri
        if (userBook.Status == ReadingStatus.Read && userBook.FinishedYear.HasValue)
        {
            if (DateValidator.IsStartedAfterFinished(targetStartDate, userBook.FinishedYear, userBook.FinishedMonth, userBook.FinishedDay))
            {
                var startedStr = DateValidator.FormatCalendarDate(targetStartDate);
                var finStr = DateValidator.FormatPartialDate(userBook.FinishedYear, userBook.FinishedMonth, userBook.FinishedDay);
                throw new ArgumentException($"Başlangıç tarihi ({startedStr}), kitabın bitirme tarihinden ({finStr}) sonra olamaz.");
            }
        }

        if (userBook.Status == ReadingStatus.DidNotFinish && userBook.DidNotFinishAt.HasValue)
        {
            if (DateValidator.IsStartedAfterDidNotFinish(targetStartDate, userBook.DidNotFinishAt.Value))
            {
                var startedStr = DateValidator.FormatCalendarDate(targetStartDate);
                var dnfStr = DateValidator.FormatCalendarDate(userBook.DidNotFinishAt.Value);
                throw new ArgumentException($"Başlangıç tarihi ({startedStr}), kitabın bırakılma tarihinden ({dnfStr}) sonra olamaz.");
            }
        }

        // 5. Mutation-last: Entity güncelleme ve kaydetme
        userBook.StartedAt = targetStartDate;
        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request)
    {
        var hasRating = request.Rating.HasValue;
        var hasReview = request.Review is not null;

        if (!hasRating && !hasReview)
            throw new ArgumentException("Puan veya yorum alanlarından en az biri belirtilmelidir.");

        if (hasRating)
        {
            var rating = request.Rating!.Value;
            if (rating < 0.5m || rating > 5.0m || rating % 0.5m != 0)
                throw new ArgumentException("Puan 0.5 ile 5.0 arasında ve 0.5'in katı olmalıdır.");
        }

        if (hasReview && request.Review!.Length > 2000)
            throw new ArgumentException("Yorum en fazla 2000 karakter olabilir.");

        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        if (userBook.Status is not (ReadingStatus.Read or ReadingStatus.DidNotFinish))
            throw new InvalidOperationException("Yalnızca 'Okundu' veya 'Yarım Bırakıldı' durumundaki kitaplara puan veya yorum eklenebilir.");

        if (hasRating)
            userBook.Rating = request.Rating;

        if (hasReview)
            userBook.Review = string.IsNullOrWhiteSpace(request.Review) ? null : request.Review.Trim();

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    public async Task<UserBookDto> SetFavoriteAsync(int userId, int userBookId, SetFavoriteRequest request)
    {
        var userBook = await _userBookRepository.GetByIdWithBookAsync(userBookId, userId);
        if (userBook is null)
            throw new KeyNotFoundException("Kitaplık kaydı bulunamadı.");

        if (request.IsFavorite && userBook.Status != ReadingStatus.Read)
            throw new InvalidOperationException("Yalnızca 'Okundu' (Read) durumundaki kitaplar favorilere eklenebilir.");

        userBook.IsFavorite = request.IsFavorite;

        await _userBookRepository.UpdateAsync(userBook);

        return MapToDto(userBook);
    }

    private static void ApplyStatusChange(UserBook userBook, ReadingStatus newStatus)
    {
        userBook.Status = newStatus;

        // Bitirme tarihi ve favori sadece "Read" durumuna aittir: kitap Read değilse ikisi de temizlenir
        if (newStatus != ReadingStatus.Read)
        {
            userBook.FinishedYear = null;
            userBook.FinishedMonth = null;
            userBook.FinishedDay = null;
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
                // Manuel veya otomatik tamamlanma tarih ataması ilgili özel metotlarda yönetilir.
                // Burada otomatik varsayılan tarih ATANMAZ.
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
        FinishedYear = userBook.FinishedYear,
        FinishedMonth = userBook.FinishedMonth,
        FinishedDay = userBook.FinishedDay,
        DidNotFinishAt = userBook.DidNotFinishAt,
        AddedAt = userBook.AddedAt
    };

    private static double? CalculateProgressPercentage(UserBook userBook)
    {
        if (userBook.Status == ReadingStatus.Read)
            return 100;

        var pageCount = userBook.Book.PageCount;
        if (pageCount is null or <= 0 || userBook.CurrentPage is null)
            return null;

        var percentage = (double)userBook.CurrentPage.Value / pageCount.Value * 100;

        return Math.Round(Math.Min(percentage, 100), 1);
    }
}
