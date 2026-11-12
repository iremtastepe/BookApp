using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Domain.Entities;
using BookApp.Domain.Enums;
using BookApp.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
 
namespace BookApp.Tests;
 
// 7.1 - Kitap arşivleme: Adım 5 (controller endpoint'leri ve HTTP hata eşlemeleri).
// Tamamı gerçek HTTP istekleriyle çalışır: gerçek controller, servis, repository ve (InMemory) veritabanı.
public class ArchiveUserBookEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string GuardMessage = "Bu kitap arşivde olduğu için güncellenemez. Önce arşivden geri yükleyin.";
    private static readonly DateTime Started = new(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
 
    private readonly CustomWebApplicationFactory _factory;
 
    public ArchiveUserBookEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }
 
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
 
    // Mevcut test sınıflarındaki yardımcının kısaltılmış kopyası. role verilirse kullanıcı o role eklenir.
    private async Task<HttpClient> CreateUserClientAsync(string email, string? role = null)
    {
        const string password = "Password123!";
 
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
 
            if (role != null && !await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<int>(role));
            }
 
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
 
            if (role != null && !await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
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
        return client;
    }
 
    // Yeni katalog kitabı (200 sayfa) oluşturur, kullanıcının kitaplığına ekler ve istenen duruma uygun alanları doğrudan yazar.
    private async Task<(int UserBookId, BookResponse Book)> CreateUserBookAsync(HttpClient client, string label, ReadingStatus status = ReadingStatus.NotStarted)
    {
        var createRes = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = $"{label} {Guid.NewGuid():N}",
            Author = "Arsiv Endpoint Yazari",
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
 
        return (userBook!.Id, book);
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
 
    // Sunucu JSON'u Türkçe karakterleri \uXXXX olarak kaçırabilir; bu yüzden metni ham string yerine JSON olarak okuruz.
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetString();
    }
 
    private static Task<HttpResponseMessage> PostArchiveAsync(HttpClient client, int userBookId)
        => client.PostAsync($"/api/library/{userBookId}/archive", null);
 
    private static Task<HttpResponseMessage> PostRestoreAsync(HttpClient client, int userBookId)
        => client.PostAsync($"/api/library/{userBookId}/restore", null);
 
    // Altı yazma endpoint'inin her biri, Read durumundaki kayıt için GEÇERLİ bir istek gönderir;
    // yani arşiv koruması olmasaydı hepsi 200 dönerdi.
    private static Task<HttpResponseMessage> SendWriteAsync(HttpClient client, string operation, int userBookId) => operation switch
    {
        "status" => client.PutAsJsonAsync($"/api/library/{userBookId}/status", new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading }),
        "progress" => client.PutAsJsonAsync($"/api/library/{userBookId}/progress", new UpdateProgressRequest { CurrentPage = 200 }),
        "finished-date" => client.PutAsJsonAsync($"/api/library/{userBookId}/finished-date", new UpdateFinishedDateRequest
        {
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 6, Day = 1 }
        }),
        "started-date" => client.PutAsJsonAsync($"/api/library/{userBookId}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 1 }
        }),
        "review" => client.PutAsJsonAsync($"/api/library/{userBookId}/review", new UpdateReviewAndRatingRequest { Rating = 5.0m }),
        "favorite" => client.PutAsJsonAsync($"/api/library/{userBookId}/favorite", new SetFavoriteRequest { IsFavorite = false }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };
 
    private static readonly string[] WriteOperations = { "status", "progress", "finished-date", "started-date", "review", "favorite" };
 
    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.NextUp)]
    [InlineData(ReadingStatus.Read)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task ArchiveAndRestore_Return200AndPreserveEveryFieldExceptArchivedAt(ReadingStatus status)
    {
        var client = await CreateUserClientAsync($"arch5_cycle_{status}@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Dongu", status);
        var before = await SnapshotAsync(userBookId);
 
        var archiveRes = await PostArchiveAsync(client, userBookId);
        Assert.Equal(HttpStatusCode.OK, archiveRes.StatusCode);
        var archivedDto = await archiveRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.NotNull(archivedDto!.ArchivedAt);
        Assert.Equal(status, archivedDto.Status);
        Assert.Equal(before, await SnapshotAsync(userBookId));
        Assert.NotNull(await GetArchivedAtAsync(userBookId));
 
        var restoreRes = await PostRestoreAsync(client, userBookId);
        Assert.Equal(HttpStatusCode.OK, restoreRes.StatusCode);
        var restoredDto = await restoreRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Null(restoredDto!.ArchivedAt);
        Assert.Equal(status, restoredDto.Status);
        Assert.Equal(before, await SnapshotAsync(userBookId));
        Assert.Null(await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task Archive_AlreadyArchived_Returns409AndKeepsOriginalArchivedAt()
    {
        var client = await CreateUserClientAsync("arch5_double@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Cift Arsivleme");
        (await PostArchiveAsync(client, userBookId)).EnsureSuccessStatusCode();
        var archivedAtBefore = await GetArchivedAtAsync(userBookId);
 
        var res = await PostArchiveAsync(client, userBookId);
 
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Bu kitap zaten arşivde.", await ReadMessageAsync(res));
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task Restore_NotArchived_Returns409()
    {
        var client = await CreateUserClientAsync("arch5_restore_active@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Aktif Kitap");
 
        var res = await PostRestoreAsync(client, userBookId);
 
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Bu kitap arşivde değil.", await ReadMessageAsync(res));
        Assert.Null(await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task ArchiveAndRestore_OtherUsersOrMissingRecord_Return404AndChangeNothing()
    {
        var owner = await CreateUserClientAsync("arch5_owner@test.com");
        var intruder = await CreateUserClientAsync("arch5_intruder@test.com");
 
        var (activeId, _) = await CreateUserBookAsync(owner, "Baskasinin Aktif");
        var (archivedId, _) = await CreateUserBookAsync(owner, "Baskasinin Arsivli");
        await ArchiveDirectAsync(archivedId);
        var archivedAtBefore = await GetArchivedAtAsync(archivedId);
 
        Assert.Equal(HttpStatusCode.NotFound, (await PostArchiveAsync(intruder, activeId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRestoreAsync(intruder, archivedId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostArchiveAsync(owner, 999999)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRestoreAsync(owner, 999999)).StatusCode);
 
        Assert.Null(await GetArchivedAtAsync(activeId));
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(archivedId));
    }
 
    [Fact]
    public async Task ArchiveAndRestore_WithoutToken_Return401()
    {
        var anonymous = _factory.CreateClient();
 
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostArchiveAsync(anonymous, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRestoreAsync(anonymous, 1)).StatusCode);
    }
 
    [Theory]
    [InlineData("status")]
    [InlineData("progress")]
    [InlineData("finished-date")]
    [InlineData("started-date")]
    [InlineData("review")]
    [InlineData("favorite")]
    public async Task WriteEndpoint_OnArchivedRecord_Returns409AndChangesNothing(string operation)
    {
        var client = await CreateUserClientAsync($"arch5_guard_{operation}@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Guard", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
        var archivedAtBefore = await GetArchivedAtAsync(userBookId);
 
        var res = await SendWriteAsync(client, operation, userBookId);
 
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(GuardMessage, await ReadMessageAsync(res));
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task InvalidBody_OnArchivedRecord_StillReturns400()
    {
        // status ve review gövdeyi kayıt aramadan ÖNCE doğrular: mevcut 400 davranışı arşivli kayıtta da korunur.
        var client = await CreateUserClientAsync("arch5_validation@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Dogrulama", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
 
        var statusRes = await client.PutAsJsonAsync($"/api/library/{userBookId}/status",
            new UpdateUserBookStatusRequest { Status = (ReadingStatus)999 });
        Assert.Equal(HttpStatusCode.BadRequest, statusRes.StatusCode);
 
        var reviewRes = await client.PutAsJsonAsync($"/api/library/{userBookId}/review", new UpdateReviewAndRatingRequest());
        Assert.Equal(HttpStatusCode.BadRequest, reviewRes.StatusCode);
 
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
    }
 
    [Fact]
    public async Task WriteEndpoints_OnOtherUsersArchivedRecord_Return404NotConflict()
    {
        // Başkasının arşivli kaydı için "arşivde" bilgisi sızmamalı: 404 davranışı korunur.
        var owner = await CreateUserClientAsync("arch5_guard_owner@test.com");
        var intruder = await CreateUserClientAsync("arch5_guard_intruder@test.com");
        var (userBookId, _) = await CreateUserBookAsync(owner, "Baskasinin Arsivli Kitabi", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
 
        foreach (var operation in WriteOperations)
        {
            var res = await SendWriteAsync(intruder, operation, userBookId);
            Assert.True(res.StatusCode == HttpStatusCode.NotFound, $"{operation}: beklenen 404, gelen {(int)res.StatusCode}");
        }
 
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
    }
 
    [Fact]
    public async Task AfterRestore_WriteEndpointsWorkAgain()
    {
        var client = await CreateUserClientAsync("arch5_after_restore@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Geri Yuklenen", ReadingStatus.Read);
 
        (await PostArchiveAsync(client, userBookId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await SendWriteAsync(client, "favorite", userBookId)).StatusCode);
 
        (await PostRestoreAsync(client, userBookId)).EnsureSuccessStatusCode();
        var res = await SendWriteAsync(client, "favorite", userBookId);
 
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.False(dto!.IsFavorite);
        Assert.Null(dto.ArchivedAt);
    }
 
    [Fact]
    public async Task ArchiveAndRestore_MoveBookBetweenDefaultAndArchiveLists()
    {
        var client = await CreateUserClientAsync("arch5_lists@test.com");
        var (userBookId, _) = await CreateUserBookAsync(client, "Liste Kitabi");
 
        (await PostArchiveAsync(client, userBookId)).EnsureSuccessStatusCode();
 
        var defaultAfterArchive = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library");
        Assert.Empty(defaultAfterArchive!.Items);
        var archiveList = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library?archived=true");
        var archivedItem = Assert.Single(archiveList!.Items);
        Assert.Equal(userBookId, archivedItem.Id);
        Assert.NotNull(archivedItem.ArchivedAt);
 
        (await PostRestoreAsync(client, userBookId)).EnsureSuccessStatusCode();
 
        var defaultAfterRestore = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library");
        var restoredItem = Assert.Single(defaultAfterRestore!.Items);
        Assert.Equal(userBookId, restoredItem.Id);
        Assert.Null(restoredItem.ArchivedAt);
        var archiveListAfter = await client.GetFromJsonAsync<PagedResponse<UserBookDto>>("/api/library?archived=true");
        Assert.Empty(archiveListAfter!.Items);
    }
 
    // ---- 7.4-B ile uyum: arşivli kayıtlar admin PageCount eşitlemesi ve kitap silme korumasında da hesaba katılır ----
 
    [Fact]
    public async Task AdminPageCountChange_AlsoSyncsArchivedReadRecord_AndKeepsItArchived()
    {
        var admin = await CreateUserClientAsync("arch5_admin_sync@test.com", "Admin");
        var user = await CreateUserClientAsync("arch5_user_sync@test.com");
        var (userBookId, book) = await CreateUserBookAsync(user, "Esitleme", ReadingStatus.Read);
        await ArchiveDirectAsync(userBookId);
        var archivedAtBefore = await GetArchivedAtAsync(userBookId);
 
        var putRes = await admin.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 300
        });
 
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var after = await SnapshotAsync(userBookId);
        // Yeni mimaride admin katalog sayfasını değiştirse bile arşivli kullanıcının kendi CurrentPage'i (200) korunur.
        Assert.Equal(200, after.CurrentPage);
        Assert.Equal(4.5m, after.Rating);
        Assert.True(after.IsFavorite);
        Assert.Equal(archivedAtBefore, await GetArchivedAtAsync(userBookId));
    }
 
    [Fact]
    public async Task AdminPageCountBelowArchivedReadingRecord_DoesNotBlockAdminUpdate_Returns200Ok()
    {
        var admin = await CreateUserClientAsync("arch5_admin_limit@test.com", "Admin");
        var user = await CreateUserClientAsync("arch5_user_limit@test.com");
        var (userBookId, book) = await CreateUserBookAsync(user, "Sinir", ReadingStatus.Reading);
        await ArchiveDirectAsync(userBookId);
        var snapshotBefore = await SnapshotAsync(userBookId);
 
        // Admin katalog sayfa sayısını 40 yapar; yeni modelde kullanıcının ilerlemesi admini engellemez (200 OK döner)
        var putRes = await admin.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 40
        });
 
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        Assert.Equal(snapshotBefore, await SnapshotAsync(userBookId));
 
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var freshBook = await db.Books.AsNoTracking().FirstAsync(b => b.Id == book.Id);
        Assert.Equal(40, freshBook.PageCount);
    }
 
    [Fact]
    public async Task AdminDeleteBook_WithOnlyArchivedUserBook_Returns409AndKeepsBook()
    {
        var admin = await CreateUserClientAsync("arch5_admin_delete@test.com", "Admin");
        var user = await CreateUserClientAsync("arch5_user_delete@test.com");
        var (userBookId, book) = await CreateUserBookAsync(user, "Silinemez");
        await ArchiveDirectAsync(userBookId);
 
        var deleteRes = await admin.DeleteAsync($"/api/books/{book.Id}");
 
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        var getRes = await user.GetAsync($"/api/books/{book.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        Assert.NotNull(await GetArchivedAtAsync(userBookId));
    }
}
 