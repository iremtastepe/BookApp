using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
 
namespace BookApp.Tests;
 
// 7.1 - Kitap arşivleme: Adım 4 (servis mantığı).
// Controller eşlemesi (409) Adım 5'te olduğu için bu dosya IUserBookService'i DI kapsamından çözüp doğrudan çağırır.
// Gerçek servis, repository ve (InMemory) veritabanı çalışır. Kitap ve kitaplık kaydı hazırlığı HTTP üzerinden yapılır.
public class ArchiveUserBookServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string GuardMessage = "Bu kitap arşivde olduğu için güncellenemez. Önce arşivden geri yükleyin.";
    private static readonly DateTime Started = new(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
 
    private readonly CustomWebApplicationFactory _factory;
 
    public ArchiveUserBookServiceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }
 
    // Kullanıcının tüm kullanıcıya-özel alanlarının anlık görüntüsü (ArchivedAt hariç). record: değer eşitliği.
    private sealed record UserBookSnapshot(
        ReadingStatus Status,
        bool IsFavorite,
        decimal? Rating,
        string? Review,
        int? CurrentPage,
        DateTime? StartedAt,
        int? FinishedYear,
        int? FinishedMonth,
        int? FinishedDay,
        DateTime? DidNotFinishAt,
        DateTime AddedAt);
 
    // Mevcut test sınıflarındaki yardımcının kısaltılmış kopyası; kullanıcı Id'sini de döndürür.
    private async Task<(HttpClient Client, int UserId)> CreateUserAsync(string email)
    {
        const string password = "Password123!";
        int userId;
 
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new User { UserName = email.Split('@')[0], Email = email };
                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    throw new Exception("Test kullanıcısı oluşturulamadı: " + string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
            userId = user.Id;
        }
 
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
 
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        return (client, userId);
    }
 
    // Yeni katalog kitabı (200 sayfa) oluşturur, kullanıcının kitaplığına ekler ve istenen duruma uygun alanları doğrudan yazar.
    private async Task<(int UserBookId, int BookId)> CreateUserBookAsync(HttpClient client, string label, ReadingStatus status = ReadingStatus.NotStarted)
    {
        var createRes = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = $"{label} {Guid.NewGuid():N}",
            Author = "Arsiv Servis Yazari",
            PageCount = 200
        });
        createRes.EnsureSuccessStatusCode();
        var book = await createRes.Content.ReadFromJsonAsync<BookResponse>();
 
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        addRes.EnsureSuccessStatusCode();
        var userBook = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
 
        if (status != ReadingStatus.NotStarted)
        {
            await MutateDirectAsync(userBook!.Id, ub =>
            {
                ub.Status = status;
                switch (status)
                {
                    case ReadingStatus.Reading:
                        ub.CurrentPage = 50;
                        ub.StartedAt = Started;
                        break;
                    case ReadingStatus.Read:
                        ub.IsFavorite = true;
                        ub.Rating = 4.5m;
                        ub.Review = "Arsiv test yorumu";
                        ub.CurrentPage = 200;
                        ub.StartedAt = Started;
                        ub.FinishedYear = 2024;
                        ub.FinishedMonth = 5;
                        ub.FinishedDay = 20;
                        break;
                    case ReadingStatus.DidNotFinish:
                        ub.CurrentPage = 80;
                        ub.StartedAt = Started;
                        ub.DidNotFinishAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc);
                        ub.Rating = 3.0m;
                        ub.Review = "Yarim biraktim";
                        break;
                }
            });
        }
 
        return (userBook!.Id, book.Id);
    }
 
    private async Task MutateDirectAsync(int userBookId, Action<UserBook> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userBook = await db.UserBooks.FirstAsync(x => x.Id == userBookId);
        mutate(userBook);
        await db.SaveChangesAsync();
    }
 
    private Task ArchiveDirectAsync(int userBookId)
        => MutateDirectAsync(userBookId, ub => ub.ArchivedAt = DateTime.UtcNow);
 
    private async Task<UserBookSnapshot> SnapshotAsync(int userBookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ub = await db.UserBooks.AsNoTracking().FirstAsync(x => x.Id == userBookId);
        return new UserBookSnapshot(
            ub.Status, ub.IsFavorite, ub.Rating, ub.Review, ub.CurrentPage, ub.StartedAt,
            ub.FinishedYear, ub.FinishedMonth, ub.FinishedDay, ub.DidNotFinishAt, ub.AddedAt);
    }
 
    private async Task<DateTime?> GetArchivedAtAsync(int userBookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ub = await db.UserBooks.AsNoTracking().FirstAsync(x => x.Id == userBookId);
        return ub.ArchivedAt;
    }
 
    // Gerçek servisi, her çağrı için yeni bir DI kapsamında çalıştırır (controller isteği gibi).
    private async Task<T> WithServiceAsync<T>(Func<IUserBookService, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserBookService>();
        return await action(service);
    }
 
    // Dönüş değeri gerekmeyen çağrılar için: istisnalar olduğu gibi yukarı taşınır.
    private async Task RunServiceAsync(Func<IUserBookService, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserBookService>();
        await action(service);
    }
 
    // Altı yazma işleminin her biri, Read durumundaki kayıt için GEÇERLİ bir istek gönderir;
    // yani guard olmasaydı hepsi başarılı olurdu.
    private static Task CallWriteAsync(IUserBookService service, string operation, int userId, int userBookId) => operation switch
    {
        "status" => service.UpdateStatusAsync(userId, userBookId, new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading }),
        "progress" => service.UpdateProgressAsync(userId, userBookId, new UpdateProgressRequest { CurrentPage = 200 }),
        "finished-date" => service.UpdateFinishedDateAsync(userId, userBookId, new UpdateFinishedDateRequest
        {
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 6, Day = 1 }
        }),
        "started-date" => service.UpdateStartedDateAsync(userId, userBookId, new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 1 }
        }),
        "review" => service.UpdateReviewAndRatingAsync(userId, userBookId, new UpdateReviewAndRatingRequest { Rating = 5.0m }),
        "favorite" => service.SetFavoriteAsync(userId, userBookId, new SetFavoriteRequest { IsFavorite = false }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };
 
    private static readonly string[] WriteOperations = { "status", "progress", "finished-date", "started-date", "review", "favorite" };
 
    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.NextUp)]
    [InlineData(ReadingStatus.Read)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task ArchiveThenRestore_PreservesEveryFieldExceptArchivedAt(ReadingStatus status)
    {
        var (client, userId) = await CreateUserAsync($"arch4_cycle_{status}@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Dongu", status);
        var before = await SnapshotAsync(userBookId);
 
        var archivedDto = await WithServiceAsync(s => s.ArchiveAsync(userId, userBookId));
        Assert.NotNull(archivedDto.ArchivedAt);
        Assert.Equal(status, archivedDto.Status);
        Assert.Equal(before, await SnapshotAsync(userBookId));
        Assert.NotNull(await GetArchivedAtAsync(userBookId));
 
        var restoredDto = await WithServiceAsync(s => s.RestoreAsync(userId, userBookId));
        Assert.Null(restoredDto.ArchivedAt);
        Assert.Equal(status, restoredDto.Status);
        Assert.Equal(before, await SnapshotAsync(userBookId));
        Assert.Null(await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task Archive_AlreadyArchived_ThrowsInvalidOperationAndKeepsOriginalArchivedAt()
    {
        var (client, userId) = await CreateUserAsync("arch4_double@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Cift Arsivleme");
        await WithServiceAsync(s => s.ArchiveAsync(userId, userBookId));
        var archivedAtBefore = await GetArchivedAtAsync(userBookId);
 
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WithServiceAsync(s => s.ArchiveAsync(userId, userBookId)));
 
        Assert.Equal("Bu kitap zaten arşivde.", ex.Message);
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task Restore_NotArchived_ThrowsInvalidOperation()
    {
        var (client, userId) = await CreateUserAsync("arch4_restore_active@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Aktif Kitap");
        var before = await SnapshotAsync(userBookId);
 
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WithServiceAsync(s => s.RestoreAsync(userId, userBookId)));
 
        Assert.Equal("Bu kitap arşivde değil.", ex.Message);
        Assert.Null(await GetArchivedAtAsync(userBookId));
        Assert.Equal(before, await SnapshotAsync(userBookId));
    }
 
    [Fact]
    public async Task ArchiveAndRestore_OnOtherUsersOrMissingRecord_ThrowKeyNotFoundAndChangeNothing()
    {
        var (client1, user1) = await CreateUserAsync("arch4_owner@test.com");
        var (_, user2) = await CreateUserAsync("arch4_intruder@test.com");
 
        var (activeId, _) = await CreateUserBookAsync(client1, "Baskasinin Aktif");
        var (archivedId, _) = await CreateUserBookAsync(client1, "Baskasinin Arsivli");
        await ArchiveDirectAsync(archivedId);
        var archivedAtBefore = await GetArchivedAtAsync(archivedId);
 
        await Assert.ThrowsAsync<KeyNotFoundException>(() => WithServiceAsync(s => s.ArchiveAsync(user2, activeId)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => WithServiceAsync(s => s.RestoreAsync(user2, archivedId)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => WithServiceAsync(s => s.ArchiveAsync(user1, 999999)));
 
        Assert.Null(await GetArchivedAtAsync(activeId));
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(archivedId));
    }
 
    [Theory]
    [InlineData("status")]
    [InlineData("progress")]
    [InlineData("finished-date")]
    [InlineData("started-date")]
    [InlineData("review")]
    [InlineData("favorite")]
    public async Task WriteOperation_OnArchivedRecord_ThrowsArchivedExceptionAndChangesNothing(string operation)
    {
        var (client, userId) = await CreateUserAsync($"arch4_guard_{operation}@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Guard", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
        var archivedAtBefore = await GetArchivedAtAsync(userBookId);
 
        var ex = await Assert.ThrowsAsync<ArchivedUserBookException>(
            () => RunServiceAsync(s => CallWriteAsync(s, operation, userId, userBookId)));
 
        Assert.Equal(GuardMessage, ex.Message);
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task InvalidBody_OnArchivedRecord_StillReturnsValidationErrorNotArchivedException()
    {
        // status ve review metotları gövdeyi kayıt aramadan ÖNCE doğrular: mevcut 400 davranışı arşivli kayıtta da korunur.
        var (client, userId) = await CreateUserAsync("arch4_validation@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Dogrulama", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
 
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(s =>
            s.UpdateStatusAsync(userId, userBookId, new UpdateUserBookStatusRequest { Status = (ReadingStatus)999 })));
 
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(s =>
            s.UpdateReviewAndRatingAsync(userId, userBookId, new UpdateReviewAndRatingRequest())));
    }
 
    [Fact]
    public async Task WriteOperations_OnOtherUsersArchivedRecord_ThrowKeyNotFoundNotArchivedException()
    {
        // Başkasının arşivli kaydı için "arşivde" bilgisi sızmamalı: 404 davranışı korunur.
        var (client1, _) = await CreateUserAsync("arch4_guard_owner@test.com");
        var (_, intruderId) = await CreateUserAsync("arch4_guard_intruder@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client1, "Baskasinin Arsivli Kitabi", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
 
        foreach (var operation in WriteOperations)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => RunServiceAsync(s => CallWriteAsync(s, operation, intruderId, userBookId)));
        }
 
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
    }
 
    [Fact]
    public async Task AfterRestore_WriteOperationsWorkAgain()
    {
        var (client, userId) = await CreateUserAsync("arch4_after_restore@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Geri Yuklenen", ReadingStatus.Read);
 
        await WithServiceAsync(s => s.ArchiveAsync(userId, userBookId));
        await Assert.ThrowsAsync<ArchivedUserBookException>(() => WithServiceAsync(s =>
            s.SetFavoriteAsync(userId, userBookId, new SetFavoriteRequest { IsFavorite = false })));
 
        await WithServiceAsync(s => s.RestoreAsync(userId, userBookId));
        var dto = await WithServiceAsync(s =>
            s.SetFavoriteAsync(userId, userBookId, new SetFavoriteRequest { IsFavorite = false }));
 
        Assert.False(dto.IsFavorite);
        Assert.Null(dto.ArchivedAt);
    }
 
    [Fact]
    public async Task ArchivedAt_IsReturnedInLibraryDto_OnlyWhenArchived()
    {
        var (client, userId) = await CreateUserAsync("arch4_dto@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "DTO Kitabi");
 
        var activeList = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library");
        Assert.Null(Assert.Single(activeList!.Items).ArchivedAt);
 
        await WithServiceAsync(s => s.ArchiveAsync(userId, userBookId));
 
        var archivedList = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library?archived=true");
        Assert.NotNull(Assert.Single(archivedList!.Items).ArchivedAt);
 
        var defaultAfter = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library");
        Assert.Empty(defaultAfter!.Items);
    }
 
    [Fact]
    public async Task AddToLibrary_ForArchivedBook_ThrowsInvalidOperationWithArchiveHintAndKeepsSingleRecord()
    {
        var (client, userId) = await CreateUserAsync("arch4_readd@test.com");
        var (userBookId, bookId) = await CreateUserBookAsync(client, "Tekrar Ekleme");
        await ArchiveDirectAsync(userBookId);
 
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WithServiceAsync(s =>
            s.AddToLibraryAsync(userId, new AddToLibraryRequest { BookId = bookId })));
 
        Assert.Equal(
            "Bu kitap zaten kitaplığınızda ekli veya arşivinizde. Arşivdeyse arşiv listenizden geri yükleyebilirsiniz.",
            ex.Message);
 
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserBooks.CountAsync(ub => ub.BookId == bookId));
    }
}