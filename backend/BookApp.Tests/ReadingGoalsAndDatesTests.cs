using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BookApp.Application.DTOs.Goals;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Helpers;
using BookApp.Application.Interfaces;
using BookApp.Application.Services;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using Xunit;

namespace BookApp.Tests;

public class ReadingGoalsAndDatesTests
{
    // --- 1. DateValidator Testleri ---
    [Fact]
    public void DateValidator_ValidDates_ShouldPass()
    {
        // Geçmiş tam tarih
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2022, Month = 5, Day = 20 });

        // Sadece geçmiş yıl
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2020 });

        // Geçmiş ay ve yıl
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2023, Month = 8 });

        // Artık yılda 29 Şubat (2024 artık yıldır)
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2024, Month = 2, Day = 29 });
    }

    [Fact]
    public void DateValidator_InvalidHierarchy_ShouldThrow()
    {
        // Gün var ama ay/yıl yok
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Day = 15 }));

        // Ay var ama yıl yok
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Month = 5 }));

        // Boş nesne
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto()));
    }

    [Fact]
    public void DateValidator_InvalidCalendarDates_ShouldThrow()
    {
        // Nisan 30 çeker, 31 girilemez
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2023, Month = 4, Day = 31 }));

        // 2023 artık yıl değildir, 29 Şubat girilemez
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2023, Month = 2, Day = 29 }));

        // Geçersiz ay (13)
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2023, Month = 13 }));

        // Geçersiz yıl (0 veya negatif)
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 0 }));
    }

    [Fact]
    public void DateValidator_FutureDates_ShouldThrow()
    {
        // Uzak gelecek yıl
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2099 }));

        // 1 günlük tolerans sınırından daha ileri bir tam tarih
        var farFuture = DateTime.UtcNow.AddDays(5);
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = farFuture.Year, Month = farFuture.Month, Day = farFuture.Day }));
    }

    // --- 2. UserBookService Tarih ve Durum Kuralları Testleri ---
    private class FakeUserBookRepository : IUserBookRepository
    {
        public List<UserBook> Books = new();

        public Task<bool> ExistsAsync(int userId, int bookId)
            => Task.FromResult(Books.Any(b => b.UserId == userId && b.BookId == bookId));

        public Task<UserBook> AddAsync(UserBook userBook)
        {
            userBook.Id = Books.Count + 1;
            Books.Add(userBook);
            return Task.FromResult(userBook);
        }

        public Task<UserBook?> GetByIdWithBookAsync(int id, int userId)
            => Task.FromResult(Books.FirstOrDefault(b => b.Id == id && b.UserId == userId));

        public Task<List<UserBook>> GetAllByUserIdAsync(int userId)
            => Task.FromResult(Books.Where(b => b.UserId == userId).ToList());

        public Task<(List<UserBook> Items, int TotalCount)> GetPagedByUserIdAsync(int userId, LibraryQuery query)
            => Task.FromResult((Books.Where(b => b.UserId == userId).ToList(), Books.Count(b => b.UserId == userId)));

        public Task UpdateAsync(UserBook userBook)
            => Task.CompletedTask;

        public Task<int> GetReadBooksCountByYearAsync(int userId, int year)
            => Task.FromResult(Books.Count(b => b.UserId == userId && b.Status == ReadingStatus.Read && b.FinishedYear == year));

        public Task<int> GetTotalReadBooksCountAsync(int userId)
            => Task.FromResult(Books.Count(b => b.UserId == userId && b.Status == ReadingStatus.Read));

        public Task<(List<UserBook> Items, int TotalCount)> GetMonthlyReadBooksPagedAsync(int userId, int year, int month, int page, int pageSize)
        {
            var items = Books.Where(b => b.UserId == userId && b.Status == ReadingStatus.Read && b.FinishedYear == year && b.FinishedMonth == month).ToList();
            return Task.FromResult((items, items.Count));
        }
    }

    private class FakeBookRepository : IBookRepository
    {
        public Task<Book?> GetByIdAsync(int id)
            => Task.FromResult<Book?>(new Book { Id = id, Title = "Test Kitap", Author = "Test Yazar", PageCount = 200 });

        public Task<Book?> GetByIsbnAsync(string isbn) => Task.FromResult<Book?>(null);
        public Task<(List<Book> Items, int TotalCount)> GetPagedAsync(string? search, int page, int pageSize) => Task.FromResult((new List<Book>(), 0));
        public Task<bool> IsInAnyLibraryAsync(int bookId) => Task.FromResult(false);
        public Task AddAsync(Book book) => Task.CompletedTask;
        public Task UpdateAsync(Book book) => Task.CompletedTask;
        public Task DeleteAsync(Book book) => Task.CompletedTask;
    }

    [Fact]
    public async Task UpdateStatus_ToRead_WithoutDateOrUnknown_ShouldThrow400()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            Book = new Book { Id = 1, Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Tarih yok, unknown da yok -> 400 (ArgumentException)
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest { Status = ReadingStatus.Read }));
    }

    [Fact]
    public async Task UpdateStatus_ToRead_WithBothDateAndUnknown_ShouldThrow400()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            Book = new Book { Id = 1, Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Hem tarih hem unknown aynı anda -> 400
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest
            {
                Status = ReadingStatus.Read,
                FinishedDate = new CompletionDateDto { Year = 2024 },
                FinishedDateUnknown = true
            }));
    }

    [Fact]
    public async Task UpdateStatus_WhenAlreadyRead_ShouldKeepExistingDate()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            FinishedYear = 2023,
            FinishedMonth = 5,
            FinishedDay = 10,
            Book = new Book { Id = 1, Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Tekrar Read gönderildiğinde mevcut 2023-05-10 tarihi ezilmez/temizlenmez
        var result = await service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest { Status = ReadingStatus.Read });

        Assert.Equal(2023, result.FinishedYear);
        Assert.Equal(5, result.FinishedMonth);
        Assert.Equal(10, result.FinishedDay);
    }

    [Fact]
    public async Task UpdateProgress_ToLastPage_AutomaticallySetsRead_AndLocalDate()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading,
            CurrentPage = 50,
            Book = new Book { Id = 1, Title = "A", Author = "B", PageCount = 100 }
        };
        repo.Books.Add(ub);

        // Son sayfaya (100) ilerlendiğinde opsiyonel yerel tarih atanır
        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest
        {
            CurrentPage = 100,
            LocalDate = new CompletionDateDto { Year = 2025, Month = 6, Day = 15 }
        });

        Assert.Equal(ReadingStatus.Read, result.Status);
        Assert.Equal(2025, result.FinishedYear);
        Assert.Equal(6, result.FinishedMonth);
        Assert.Equal(15, result.FinishedDay);
    }

    [Fact]
    public async Task UpdateProgress_WhenAlreadyRead_DoesNotOverwriteFinishedDate()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            CurrentPage = 100,
            FinishedYear = 2022,
            FinishedMonth = 1,
            FinishedDay = 1,
            Book = new Book { Id = 1, Title = "A", Author = "B", PageCount = 100 }
        };
        repo.Books.Add(ub);

        // Zaten Read olan kitaba tekrar son sayfa gönderilince tarih değişmez
        var result = await service.UpdateProgressAsync(1, 1, new UpdateProgressRequest
        {
            CurrentPage = 100,
            LocalDate = new CompletionDateDto { Year = 2026, Month = 10, Day = 10 }
        });

        Assert.Equal(2022, result.FinishedYear);
        Assert.Equal(1, result.FinishedMonth);
        Assert.Equal(1, result.FinishedDay);
    }

    // --- 3. ReadingGoalService İlerleme Hesaplama Testleri ---
    private class FakeGoalRepository : IReadingGoalRepository
    {
        public List<ReadingGoal> Goals = new();

        public Task<ReadingGoal?> GetByUserAndYearAsync(int userId, int year)
            => Task.FromResult(Goals.FirstOrDefault(g => g.UserId == userId && g.Year == year));

        public Task<ReadingGoal> AddAsync(ReadingGoal goal)
        {
            goal.Id = Goals.Count + 1;
            Goals.Add(goal);
            return Task.FromResult(goal);
        }

        public Task UpdateAsync(ReadingGoal goal) => Task.CompletedTask;
    }

    [Fact]
    public async Task ReadingGoalService_CalculatesProgressAndTotalReadBooksCorrectly()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        // 2026 için 20 kitap hedefi oluştur
        await goalService.CreateOrUpdateGoalAsync(1, 2026, new CreateOrUpdateReadingGoalRequest { TargetBooks = 20 });

        // Kullanıcı 2026'da 25 kitap bitirmiş (hedefi aşmış)
        for (int i = 0; i < 25; i++)
        {
            userBookRepo.Books.Add(new UserBook
            {
                Id = i + 1,
                UserId = 1,
                BookId = i + 1,
                Status = ReadingStatus.Read,
                FinishedYear = 2026,
                Book = new Book { Title = $"K{i}" }
            });
        }

        // Kullanıcı geçmişte tarihi bilinmeyen 10 kitap da okumuş
        for (int i = 0; i < 10; i++)
        {
            userBookRepo.Books.Add(new UserBook
            {
                Id = 100 + i,
                UserId = 1,
                BookId = 100 + i,
                Status = ReadingStatus.Read,
                FinishedYear = null,
                Book = new Book { Title = $"EskiK{i}" }
            });
        }

        var result = await goalService.GetAnnualGoalAsync(1, 2026);

        // 2026 hedefi: 20
        Assert.Equal(20, result.TargetBooks);
        // 2026 okunan: 25
        Assert.Equal(25, result.ReadBooksCount);
        // İlerleme yüzdesi: 25 / 20 * 100 = %125.0 (100'ü aşabilir)
        Assert.Equal(125.0, result.Percentage);
        // Kalan kitap: 0 (hedef aşıldığı için)
        Assert.Equal(0, result.RemainingBooks);
        // TotalReadBooks: 25 (2026) + 10 (tarihi bilinmeyen) = 35
        Assert.Equal(35, result.TotalReadBooks);
    }

    [Fact]
    public async Task ReadingGoalService_WhenNoGoalSet_ReturnsTargetNullWithoutError()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        userBookRepo.Books.Add(new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            FinishedYear = 2026,
            Book = new Book { Title = "K1" }
        });

        // Hedef belirlenmemiş bir yıl sorgulanıyor
        var result = await goalService.GetAnnualGoalAsync(1, 2026);

        Assert.Null(result.TargetBooks);
        Assert.Null(result.Percentage);
        Assert.Null(result.RemainingBooks);
        Assert.Equal(1, result.ReadBooksCount);
        Assert.Equal(1, result.TotalReadBooks);
    }

    [Fact]
    public async Task UpdateFinishedDate_OnReadBook_ShouldChangeDateSuccessfully()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            FinishedYear = 2024,
            FinishedMonth = 5,
            FinishedDay = 12,
            Book = new Book { Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Tarihi 2024'ten 2025-08-20'ye güncelle
        var result = await service.UpdateFinishedDateAsync(1, 1, new UpdateFinishedDateRequest
        {
            FinishedDate = new CompletionDateDto { Year = 2025, Month = 8, Day = 20 }
        });

        Assert.Equal(2025, result.FinishedYear);
        Assert.Equal(8, result.FinishedMonth);
        Assert.Equal(20, result.FinishedDay);
        Assert.Equal(new DateOnly(2025, 8, 20), result.FinishedDate);
    }

    [Fact]
    public async Task UpdateFinishedDate_SetUnknown_ShouldClearDateComponents()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            FinishedYear = 2024,
            FinishedMonth = 5,
            FinishedDay = 12,
            Book = new Book { Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Tarihi bilinmiyor yap
        var result = await service.UpdateFinishedDateAsync(1, 1, new UpdateFinishedDateRequest
        {
            FinishedDateUnknown = true
        });

        Assert.Null(result.FinishedYear);
        Assert.Null(result.FinishedMonth);
        Assert.Null(result.FinishedDay);
        Assert.Null(result.FinishedDate);
    }

    [Fact]
    public async Task UpdateFinishedDate_OnNonReadBook_ShouldThrowInvalidOperation()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Reading, // Read değil!
            Book = new Book { Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateFinishedDateAsync(1, 1, new UpdateFinishedDateRequest
            {
                FinishedDate = new CompletionDateDto { Year = 2025 }
            }));
    }

    [Fact]
    public async Task UpdateStatus_FromReadToOtherStatus_ShouldClearDateComponents()
    {
        var repo = new FakeUserBookRepository();
        var bookRepo = new FakeBookRepository();
        var service = new UserBookService(repo, bookRepo);

        var ub = new UserBook
        {
            Id = 1,
            UserId = 1,
            BookId = 1,
            Status = ReadingStatus.Read,
            FinishedYear = 2026,
            FinishedMonth = 3,
            FinishedDay = 15,
            IsFavorite = true,
            Book = new Book { Title = "A", Author = "B" }
        };
        repo.Books.Add(ub);

        // Read durumundan Reading durumuna geç
        var result = await service.UpdateStatusAsync(1, 1, new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Reading
        });

        Assert.Equal(ReadingStatus.Reading, result.Status);
        Assert.Null(result.FinishedYear);
        Assert.Null(result.FinishedMonth);
        Assert.Null(result.FinishedDay);
        Assert.Null(result.FinishedDate);
        Assert.False(result.IsFavorite);
    }

    [Fact]
    public async Task TotalReadBooks_IncludesUnknownDateReadBooks()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        // Tarihi bilinen 2 kitap
        userBookRepo.Books.Add(new UserBook { Id = 1, UserId = 1, Status = ReadingStatus.Read, FinishedYear = 2026 });
        userBookRepo.Books.Add(new UserBook { Id = 2, UserId = 1, Status = ReadingStatus.Read, FinishedYear = 2025 });

        // Tarihi bilinmeyen 3 kitap
        userBookRepo.Books.Add(new UserBook { Id = 3, UserId = 1, Status = ReadingStatus.Read, FinishedYear = null });
        userBookRepo.Books.Add(new UserBook { Id = 4, UserId = 1, Status = ReadingStatus.Read, FinishedYear = null });
        userBookRepo.Books.Add(new UserBook { Id = 5, UserId = 1, Status = ReadingStatus.Read, FinishedYear = null });

        // Read olmayan kitap (sayılmamalı)
        userBookRepo.Books.Add(new UserBook { Id = 6, UserId = 1, Status = ReadingStatus.Reading });

        var goal2026 = await goalService.GetAnnualGoalAsync(1, 2026);

        Assert.Equal(1, goal2026.ReadBooksCount);
        Assert.Equal(5, goal2026.TotalReadBooks); // 1 (2026) + 1 (2025) + 3 (bilinmeyen) = 5
    }

    [Fact]
    public async Task MonthlyAndAnnualCounts_AreProperlySeparated()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        // Kitap 1: Sadece 2026 yılı biliniyor (ay bilinmiyor)
        userBookRepo.Books.Add(new UserBook { Id = 1, UserId = 1, Status = ReadingStatus.Read, FinishedYear = 2026, FinishedMonth = null, Book = new Book { Title = "K1" } });

        // Kitap 2: Mart 2026'da bitirilmiş
        userBookRepo.Books.Add(new UserBook { Id = 2, UserId = 1, Status = ReadingStatus.Read, FinishedYear = 2026, FinishedMonth = 3, FinishedDay = 15, Book = new Book { Title = "K2" } });

        // Yıllık istatistikte ikisi de sayılmalı
        var annual = await goalService.GetAnnualGoalAsync(1, 2026);
        Assert.Equal(2, annual.ReadBooksCount);

        // Mart 2026 aylık istatistiğinde yalnızca ay bilgisi 3 olan kitap görünmeli
        var monthlyMarch = await goalService.GetMonthlySummaryAsync(1, 2026, 3, 1, 10);
        Assert.Equal(1, monthlyMarch.TotalCount);
        Assert.Single(monthlyMarch.Books);
        Assert.Equal("K2", monthlyMarch.Books[0].Title);

        // Nisan 2026 aylık istatistiğinde 0 olmalı
        var monthlyApril = await goalService.GetMonthlySummaryAsync(1, 2026, 4, 1, 10);
        Assert.Equal(0, monthlyApril.TotalCount);
        Assert.Empty(monthlyApril.Books);
    }

    [Fact]
    public async Task GetMonthlySummary_PaginationAndOrdering_WorksCorrectly()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        for (int i = 1; i <= 5; i++)
        {
            userBookRepo.Books.Add(new UserBook
            {
                Id = i,
                UserId = 1,
                Status = ReadingStatus.Read,
                FinishedYear = 2026,
                FinishedMonth = 10,
                FinishedDay = i * 2,
                Book = new Book { Title = $"Kitap {i}", Author = "Yazar" }
            });
        }

        // Sayfa 1, sayfa boyutu 2
        var page1 = await goalService.GetMonthlySummaryAsync(1, 2026, 10, page: 1, pageSize: 2);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages); // 5 / 2 = 3 sayfa
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);
    }

    [Fact]
    public async Task CreateOrUpdateGoal_WhenGoalAlreadyExists_UpdatesExistingRecord()
    {
        var goalRepo = new FakeGoalRepository();
        var userBookRepo = new FakeUserBookRepository();
        var goalService = new ReadingGoalService(goalRepo, userBookRepo);

        // İlk istek: 30 hedef oluştur
        await goalService.CreateOrUpdateGoalAsync(1, 2026, new CreateOrUpdateReadingGoalRequest { TargetBooks = 30 });
        Assert.Single(goalRepo.Goals);
        Assert.Equal(30, goalRepo.Goals[0].TargetBooks);

        // İkinci istek (aynı yıl): hedefi 40'a güncelle
        await goalService.CreateOrUpdateGoalAsync(1, 2026, new CreateOrUpdateReadingGoalRequest { TargetBooks = 40 });
        Assert.Single(goalRepo.Goals); // Yeni kayıt oluşmadı (Duplicate yok!)
        Assert.Equal(40, goalRepo.Goals[0].TargetBooks);
        Assert.NotNull(goalRepo.Goals[0].UpdatedAt);
    }

    // --- 4. Yıl / Ay / Hedef Sınır Değeri Testleri ---
    // Sınırlar sabit yıl yerine şu anki yıldan hesaplanır: yıl değişince testler bozulmaz.
    private static int CurrentYear => DateTime.UtcNow.Year;

    [Fact]
    public void DateValidator_ValidateYearRange_BoundaryValues()
    {
        // Geçerli sınırlar: 2000 ve (bu yıl + 1) dahil
        DateValidator.ValidateYearRange(2000);
        DateValidator.ValidateYearRange(CurrentYear);
        DateValidator.ValidateYearRange(CurrentYear + 1);

        // Sınırın hemen dışı ve saçma değerler
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(1999));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(CurrentYear + 2));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(1800));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(3000));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(0));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateYearRange(-1));
    }

    [Fact]
    public void DateValidator_ValidateMonth_BoundaryValues()
    {
        DateValidator.ValidateMonth(1);
        DateValidator.ValidateMonth(12);

        Assert.Throws<ArgumentException>(() => DateValidator.ValidateMonth(0));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateMonth(13));
        Assert.Throws<ArgumentException>(() => DateValidator.ValidateMonth(-1));
    }

    [Fact]
    public void DateValidator_CompletionDate_BeforeMinYear_ShouldThrow_AndMinYearBoundaryPasses()
    {
        // 2000 öncesi bitirme tarihi reddedilir (yıl, yıl+ay, tam tarih)
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 1999 }));
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 1999, Month = 12 }));
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 1999, Month = 12, Day = 31 }));
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 1995 }));

        // 2000 sınırı geçerlidir
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2000 });
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2000, Month = 1 });
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 2000, Month = 1, Day = 1 });
    }

    [Fact]
    public void DateValidator_CompletionDate_FutureRuleStillWorks()
    {
        // Gelecek yıllar hâlâ reddedilir (yıl-sonu UTC tolerans kenarına düşmemek için +2)
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = CurrentYear + 2 }));

        // 9999 ve üstü teknik sınırı: DateTime hatası yerine ArgumentException
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 10000, Month = 1, Day = 1 }));
        Assert.Throws<ArgumentException>(() =>
            DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = 99999 }));

        // Bugün ve bu yıl geçerlidir
        var today = DateTime.UtcNow.Date;
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = today.Year });
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = today.Year, Month = today.Month });
        DateValidator.ValidateCompletionDate(new CompletionDateDto { Year = today.Year, Month = today.Month, Day = today.Day });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    [InlineData(9999)]
    public async Task CreateOrUpdateGoal_InvalidTargetBooks_ShouldThrow_AndNotSave(int targetBooks)
    {
        var goalRepo = new FakeGoalRepository();
        var service = new ReadingGoalService(goalRepo, new FakeUserBookRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateOrUpdateGoalAsync(1, CurrentYear, new CreateOrUpdateReadingGoalRequest { TargetBooks = targetBooks }));

        // Geçersiz istek veritabanına (burada sahte repository'ye) hiçbir kayıt yazmamalı
        Assert.Empty(goalRepo.Goals);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(1000)]
    public async Task CreateOrUpdateGoal_ValidTargetBooks_ShouldSave(int targetBooks)
    {
        var goalRepo = new FakeGoalRepository();
        var service = new ReadingGoalService(goalRepo, new FakeUserBookRepository());

        var result = await service.CreateOrUpdateGoalAsync(1, CurrentYear, new CreateOrUpdateReadingGoalRequest { TargetBooks = targetBooks });

        Assert.Equal(targetBooks, result.TargetBooks);
        Assert.Single(goalRepo.Goals);
        Assert.Equal(targetBooks, goalRepo.Goals[0].TargetBooks);
    }

    [Fact]
    public async Task ReadingGoalService_InvalidYear_ShouldThrow_ForPutGetAndMonthly()
    {
        var goalRepo = new FakeGoalRepository();
        var service = new ReadingGoalService(goalRepo, new FakeUserBookRepository());

        var invalidYears = new[] { 0, 1800, 1999, 3000, CurrentYear + 2 };

        foreach (var year in invalidYears)
        {
            // PUT
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateOrUpdateGoalAsync(1, year, new CreateOrUpdateReadingGoalRequest { TargetBooks = 10 }));

            // GET /{year}
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.GetAnnualGoalAsync(1, year));

            // GET /monthly
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.GetMonthlySummaryAsync(1, year, 6, 1, 10));
        }

        // Hiçbir geçersiz istek kayıt oluşturmamalı
        Assert.Empty(goalRepo.Goals);
    }

    [Fact]
    public async Task ReadingGoalService_YearBoundaries_2000AndNextYear_ShouldWork()
    {
        var goalRepo = new FakeGoalRepository();
        var service = new ReadingGoalService(goalRepo, new FakeUserBookRepository());

        foreach (var year in new[] { 2000, CurrentYear + 1 })
        {
            var created = await service.CreateOrUpdateGoalAsync(1, year, new CreateOrUpdateReadingGoalRequest { TargetBooks = 10 });
            Assert.Equal(year, created.Year);

            var fetched = await service.GetAnnualGoalAsync(1, year);
            Assert.Equal(10, fetched.TargetBooks);

            var monthly = await service.GetMonthlySummaryAsync(1, year, 1, 1, 10);
            Assert.Equal(year, monthly.Year);
        }
    }

    [Fact]
    public async Task GetMonthlySummary_InvalidMonth_ShouldThrow_AndBoundariesWork()
    {
        var service = new ReadingGoalService(new FakeGoalRepository(), new FakeUserBookRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlySummaryAsync(1, CurrentYear, 0, 1, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlySummaryAsync(1, CurrentYear, 13, 1, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMonthlySummaryAsync(1, CurrentYear, -1, 1, 10));

        // Sınır aylar geçerli
        await service.GetMonthlySummaryAsync(1, CurrentYear, 1, 1, 10);
        await service.GetMonthlySummaryAsync(1, CurrentYear, 12, 1, 10);
    }

    // --- 5. Review ve Rating Güncelleme Testleri ---
    // Kural: sadece gönderilen alan güncellenir, null/gönderilmeyen alan korunur, en az bir alan zorunludur.
    private const decimal OriginalRating = 3.5m;
    private const string OriginalReview = "Eski yorum";

    // Tek kayıtlı sahte repository ve servis kurar; kayıt nesnesini de döndürür ki "değişmedi" kontrolü yapılabilsin
    private static (UserBookService Service, UserBook Record) CreateServiceWithBook(
        ReadingStatus status,
        int ownerUserId = 1,
        decimal? rating = OriginalRating,
        string? review = OriginalReview)
    {
        var repo = new FakeUserBookRepository();
        var service = new UserBookService(repo, new FakeBookRepository());

        var record = new UserBook
        {
            Id = 1,
            UserId = ownerUserId,
            BookId = 1,
            Status = status,
            Rating = rating,
            Review = review,
            Book = new Book { Id = 1, Title = "A", Author = "B" }
        };
        repo.Books.Add(record);

        return (service, record);
    }

    [Fact]
    public async Task UpdateReviewAndRating_EmptyBody_ShouldThrow_AndKeepRatingAndReview()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        // {} -> rating ve review ikisi de null
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest()));

        // Açıkça null göndermek de aynı: "gönderilmedi" sayılır
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest { Rating = null, Review = null }));

        // Kayıtlı puan ve yorum silinmemiş olmalı
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task UpdateReviewAndRating_OnlyRating_ShouldUpdateRating_AndKeepReview()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest { Rating = 4.5m });

        Assert.Equal(4.5m, result.Rating);
        Assert.Equal(OriginalReview, result.Review);
        Assert.Equal(4.5m, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task UpdateReviewAndRating_OnlyReview_ShouldUpdateReview_AndKeepRating()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest { Review = "Yeni yorum" });

        Assert.Equal("Yeni yorum", result.Review);
        Assert.Equal(OriginalRating, result.Rating);
        Assert.Equal("Yeni yorum", record.Review);
        Assert.Equal(OriginalRating, record.Rating);
    }

    [Fact]
    public async Task UpdateReviewAndRating_BothFields_ShouldUpdateBoth()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest
        {
            Rating = 5m,
            Review = "Harika"
        });

        Assert.Equal(5m, result.Rating);
        Assert.Equal("Harika", result.Review);
        Assert.Equal(5m, record.Rating);
        Assert.Equal("Harika", record.Review);
    }

    [Fact]
    public async Task UpdateReviewAndRating_NullRatingWithReview_ShouldKeepRating_AndUpdateReview()
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        // {"rating": null, "review": "Yeni yorum"}: null rating "dokunma" demektir, puan silinmez
        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest
        {
            Rating = null,
            Review = "Yeni yorum"
        });

        Assert.Equal(OriginalRating, result.Rating);
        Assert.Equal("Yeni yorum", result.Review);
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal("Yeni yorum", record.Review);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public async Task UpdateReviewAndRating_EmptyOrWhitespaceReview_ShouldClearReviewToNull_AndKeepRating(string emptyReview)
    {
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read);

        // Boş metin geçerli bir "yorumu sil" isteğidir: hata değil, 200 (burada exception yok)
        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest { Review = emptyReview });

        // Veritabanına boş metin değil null yazılır, puan olduğu gibi kalır
        Assert.Null(result.Review);
        Assert.Null(record.Review);
        Assert.Equal(OriginalRating, result.Rating);
        Assert.Equal(OriginalRating, record.Rating);
    }

    [Theory]
    [InlineData(ReadingStatus.Read)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task UpdateReviewAndRating_ReadOrDidNotFinish_ShouldBeAllowed(ReadingStatus status)
    {
        var (service, record) = CreateServiceWithBook(status);

        var result = await service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest { Rating = 2m });

        Assert.Equal(2m, result.Rating);
        Assert.Equal(2m, record.Rating);
    }

    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.NextUp)]
    public async Task UpdateReviewAndRating_OtherStatuses_ShouldThrowInvalidOperation_AndKeepData(ReadingStatus status)
    {
        var (service, record) = CreateServiceWithBook(status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateReviewAndRatingAsync(1, 1, new UpdateReviewAndRatingRequest
            {
                Rating = 5m,
                Review = "Yeni yorum"
            }));

        // Reddedilen istek hiçbir alanı değiştirmemeli
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    [Fact]
    public async Task UpdateReviewAndRating_OtherUsersRecord_ShouldThrowNotFound_AndKeepOwnersData()
    {
        // Kayıt kullanıcı 1'e ait, istek kullanıcı 2'den geliyor
        var (service, record) = CreateServiceWithBook(ReadingStatus.Read, ownerUserId: 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateReviewAndRatingAsync(2, 1, new UpdateReviewAndRatingRequest
            {
                Rating = 1m,
                Review = "Başkasının yorumu"
            }));

        // Kayıt sahibinin verisi değişmemeli
        Assert.Equal(OriginalRating, record.Rating);
        Assert.Equal(OriginalReview, record.Review);
    }

    // --- 5b. UpdateReviewAndRatingRequest DTO Doğrulama Testleri ---
    // Rating aralığı, 0.5 adımı ve 2000 karakter sınırı servise değil DTO'ya aittir.
    // [ApiController] geçersiz DTO'yu servise ulaşmadan 400 ile reddeder; yani geçersiz alan varken
    // iki alan da değişmez. Burada DTO'nun kendi kuralları doğrudan çalıştırılır.
    private static List<ValidationResult> ValidateDto(UpdateReviewAndRatingRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.3")]
    [InlineData("4.25")]
    [InlineData("5.5")]
    [InlineData("6")]
    [InlineData("-1")]
    public void UpdateReviewAndRatingRequest_InvalidRating_ShouldFailValidation(string rating)
    {
        var request = new UpdateReviewAndRatingRequest
        {
            Rating = decimal.Parse(rating, CultureInfo.InvariantCulture),
            Review = "geçerli yorum"
        };

        Assert.NotEmpty(ValidateDto(request));
    }

    [Fact]
    public void UpdateReviewAndRatingRequest_TooLongReview_ShouldFailValidation()
    {
        // 2000 karakter sınırı: 2001 karakter reddedilir
        var request = new UpdateReviewAndRatingRequest
        {
            Rating = 4m,
            Review = new string('a', 2001)
        };

        Assert.NotEmpty(ValidateDto(request));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("4.5")]
    [InlineData("5")]
    public void UpdateReviewAndRatingRequest_ValidRating_ShouldPassValidation(string rating)
    {
        var request = new UpdateReviewAndRatingRequest
        {
            Rating = decimal.Parse(rating, CultureInfo.InvariantCulture)
        };

        Assert.Empty(ValidateDto(request));
    }

    [Fact]
    public void UpdateReviewAndRatingRequest_BoundaryAndPartialBodies_ShouldPassValidation()
    {
        // 2000 karakter sınırda geçerli
        Assert.Empty(ValidateDto(new UpdateReviewAndRatingRequest { Review = new string('a', 2000) }));

        // Yalnızca review, yalnızca rating ve boş metin DTO için geçerli (boş gövdeyi servis reddeder)
        Assert.Empty(ValidateDto(new UpdateReviewAndRatingRequest { Review = "yorum" }));
        Assert.Empty(ValidateDto(new UpdateReviewAndRatingRequest { Rating = 4.5m }));
        Assert.Empty(ValidateDto(new UpdateReviewAndRatingRequest { Review = "" }));
        Assert.Empty(ValidateDto(new UpdateReviewAndRatingRequest()));
    }
}