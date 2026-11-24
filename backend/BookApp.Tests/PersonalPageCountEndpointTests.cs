using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class PersonalPageCountEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PersonalPageCountEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, User User)> CreateUserAsync(string email, string? role = null)
    {
        using var scope = _factory.Services.CreateScope();
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
            var createResult = await userManager.CreateAsync(user, "Password123!");
            if (!createResult.Succeeded)
            {
                throw new Exception("Test kullanıcısı oluşturulamadı");
            }
        }

        if (role != null && !await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "Password123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);

        return (client, user);
    }

    private async Task<BookResponse> CreateBookAsync(HttpClient client, string title, int? pageCount)
    {
        var res = await client.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = title,
            Author = "Author",
            PageCount = pageCount
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BookResponse>())!;
    }

    // 1. Yeni UserBook oluşturulurken başlangıç PageCount kopyalanır
    [Fact]
    public async Task AddToLibrary_CopiesInitialPageCount_FromBook()
    {
        var (client, _) = await CreateUserAsync("ppc_user1@test.com");
        var book = await CreateBookAsync(client, "Copy Book", 350);

        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);

        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(350, ub!.PageCount);
    }

    // 2. Katalog PageCount null ise kişisel değer de null kalır
    [Fact]
    public async Task AddToLibrary_WhenBookPageCountIsNull_UserBookPageCountIsNull()
    {
        var (client, _) = await CreateUserAsync("ppc_user2@test.com");
        var book = await CreateBookAsync(client, "Null Book", null);

        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Null(ub!.PageCount);
    }

    // 3. İki farklı kullanıcı aynı kitabı farklı PageCount ile yönetebilir
    [Fact]
    public async Task TwoUsers_CanHaveDifferentPageCounts_ForSameBook()
    {
        var (userA, _) = await CreateUserAsync("ppc_usera@test.com");
        var (userB, _) = await CreateUserAsync("ppc_userb@test.com");
        var book = await CreateBookAsync(userA, "Shared Book", 300);

        var addARes = await userA.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ubA = await addARes.Content.ReadFromJsonAsync<UserBookDto>();

        var addBRes = await userB.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ubB = await addBRes.Content.ReadFromJsonAsync<UserBookDto>();

        // User A sayfa sayısını 350 (farklı baskı) yapıyor
        var putARes = await userA.PutAsJsonAsync($"/api/library/{ubA!.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 350 });
        Assert.Equal(HttpStatusCode.OK, putARes.StatusCode);

        // User B sayfa sayısını 360 yapıyor
        var putBRes = await userB.PutAsJsonAsync($"/api/library/{ubB!.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 360 });
        Assert.Equal(HttpStatusCode.OK, putBRes.StatusCode);

        var updatedA = await putARes.Content.ReadFromJsonAsync<UserBookDto>();
        var updatedB = await putBRes.Content.ReadFromJsonAsync<UserBookDto>();

        Assert.Equal(350, updatedA!.PageCount);
        Assert.Equal(360, updatedB!.PageCount);
    }

    // 4. Geçersiz PageCount değerleri 400 döner
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public async Task UpdatePageCount_InvalidValues_Returns400BadRequest(int invalidPageCount)
    {
        var (client, _) = await CreateUserAsync("ppc_user3@test.com");
        var book = await CreateBookAsync(client, "Validation Book", 300);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        var putRes = await client.PutAsJsonAsync($"/api/library/{ub!.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = invalidPageCount });
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    // 5. Reading durumunda yeni PageCount mevcut CurrentPage'den küçükse 400 döner
    [Fact]
    public async Task UpdatePageCount_InReading_BelowCurrentPage_Returns400BadRequest()
    {
        var (client, _) = await CreateUserAsync("ppc_user4@test.com");
        var book = await CreateBookAsync(client, "Reading Limit Book", 400);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await client.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 250 });

        var putRes = await client.PutAsJsonAsync($"/api/library/{ub.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 200 });
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
        var err = await putRes.Content.ReadAsStringAsync();
        Assert.Contains("250", err);
    }

    // ===== 7.4-C.4: Yeni sayfa tutarlılığı testleri =====

    // Okundu dışındaki üç durumda toplam sayfa sayısı,
    // mevcut okuma ilerlemesinin altına düşürülemez.
    [Theory]
    [InlineData(ReadingStatus.NotStarted)]
    [InlineData(ReadingStatus.NextUp)]
    [InlineData(ReadingStatus.DidNotFinish)]
    public async Task UpdatePageCount_NonRead_BelowCurrentPage_Returns400(
        ReadingStatus targetStatus)
    {
        var (client, ub) = await CreateUserBookWithPageCountAsync(
            $"ppc_c4_{(int)targetStatus}@test.com",
            $"C4 Test Book {targetStatus}",
            400);

        // Önce 250. sayfaya ilerle
        var progressRes = await client.PutAsJsonAsync(
            $"/api/library/{ub.Id}/progress",
            new UpdateProgressRequest { CurrentPage = 250 });

        Assert.Equal(HttpStatusCode.OK, progressRes.StatusCode);

        // Kitabı test edilecek okuma durumuna al
        var statusRes = await client.PutAsJsonAsync(
            $"/api/library/{ub.Id}/status",
            new UpdateUserBookStatusRequest
            {
                Status = targetStatus
            });

        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

        // Değişiklik öncesi verileri doğrula
        var before = await GetUserBookFromLibraryAsync(client, ub.Id);

        Assert.Equal(targetStatus, before.Status);
        Assert.Equal(250, before.CurrentPage);
        Assert.Equal(400, before.PageCount);

        // Toplam sayfayı mevcut ilerlemenin altına düşürmeyi dene
        var putRes = await client.PutAsJsonAsync(
            $"/api/library/{ub.Id}/page-count",
            new UpdateUserBookPageCountRequest
            {
                PageCount = 200
            });

        // Hatalı güncelleme reddedilmeli
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);

        var error = await putRes.Content.ReadAsStringAsync();
        Assert.Contains("250", error);

        // Hata sonrasında hiçbir bilgi değişmemeli
        var after = await GetUserBookFromLibraryAsync(client, ub.Id);

        Assert.Equal(400, after.PageCount);
        Assert.Equal(250, after.CurrentPage);
        Assert.Equal(targetStatus, after.Status);
    }

    // ===== 7.4-C.4 yeni testlerin sonu =====

    // 6. Read durumunda yeni PageCount biliniyorsa CurrentPage yeni PageCount'a eşitlenir
    [Fact]
    public async Task UpdatePageCount_InRead_SyncsCurrentPageToNewPageCount()
    {
        var (client, _) = await CreateUserAsync("ppc_user5@test.com");
        var book = await CreateBookAsync(client, "Read Sync Book", 300);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await client.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        // Kişisel sayfa sayısını 350 yapıyoruz
        var putRes = await client.PutAsJsonAsync($"/api/library/{ub.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 350 });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var updated = await putRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(350, updated!.PageCount);
        Assert.Equal(350, updated.CurrentPage);
        Assert.Equal(100, updated.ProgressPercentage);
    }

    // 7. PageCount açıkça null yapıldığında CurrentPage korunur
    [Fact]
    public async Task UpdatePageCount_SetToNull_PreservesCurrentPage_AndPageCountIsNull()
    {
        var (client, _) = await CreateUserAsync("ppc_user6@test.com");
        var book = await CreateBookAsync(client, "Set Null Book", 300);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await client.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 150 });

        var putRes = await client.PutAsJsonAsync($"/api/library/{ub.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = null });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var updated = await putRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Null(updated!.PageCount);
        Assert.Equal(150, updated.CurrentPage);
        Assert.Null(updated.ProgressPercentage);
    }

    // 8. Başka kullanıcının UserBook kaydını güncellemek 404 döner
    [Fact]
    public async Task UpdatePageCount_OtherUsersRecord_Returns404NotFound()
    {
        var (userA, _) = await CreateUserAsync("ppc_user_owner@test.com");
        var (userB, _) = await CreateUserAsync("ppc_user_attacker@test.com");
        var book = await CreateBookAsync(userA, "Protected Book", 300);

        var addRes = await userA.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        var putRes = await userB.PutAsJsonAsync($"/api/library/{ub!.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 320 });
        Assert.Equal(HttpStatusCode.NotFound, putRes.StatusCode);
    }

    // 9. Arşivli kitabın PageCount'u güncellenemez
    [Fact]
    public async Task UpdatePageCount_ArchivedUserBook_Returns409Conflict()
    {
        var (client, _) = await CreateUserAsync("ppc_user_arch@test.com");
        var book = await CreateBookAsync(client, "Archived Book", 300);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        var archRes = await client.PostAsync($"/api/library/{ub!.Id}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archRes.StatusCode);

        var putRes = await client.PutAsJsonAsync($"/api/library/{ub.Id}/page-count", new UpdateUserBookPageCountRequest { PageCount = 350 });
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    // 10. Admin Book.PageCount değişince kişisel PageCount korunur
    [Fact]
    public async Task AdminUpdatesBookPageCount_UserBookPageCountRemainsIndependent()
    {
        var (admin, _) = await CreateUserAsync("ppc_admin_ind@test.com", "Admin");
        var (user, _) = await CreateUserAsync("ppc_user_ind@test.com");
        var book = await CreateBookAsync(admin, "Independent Test Book", 200);

        var addRes = await user.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        // Admin katalog sayfa sayısını 400 yapıyor
        var putBookRes = await admin.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 400
        });
        Assert.Equal(HttpStatusCode.OK, putBookRes.StatusCode);

        // Kullanıcının UserBook.PageCount değeri hala 200 kalmalıdır
        var libRes = await user.GetAsync("/api/library");
        var paged = await libRes.Content.ReadFromJsonAsync<PagedResponse<UserBookDto>>();
        var userRecord = paged!.Items.First(x => x.Id == ub!.Id);
        Assert.Equal(200, userRecord.PageCount);
    }

    // ===== 7.4-D.3: Yeni JSON güvenliği testleri =====

    private static StringContent JsonBody(string json)
        => new(json, System.Text.Encoding.UTF8, "application/json");

    private static async Task<UserBookDto> GetUserBookFromLibraryAsync(HttpClient client, int userBookId)
    {
        var res = await client.GetAsync("/api/library");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var paged = await res.Content.ReadFromJsonAsync<PagedResponse<UserBookDto>>();
        return paged!.Items.First(x => x.Id == userBookId);
    }

    private async Task<(HttpClient Client, UserBookDto UserBook)> CreateUserBookWithPageCountAsync(
        string email, string title, int pageCount)
    {
        var (client, _) = await CreateUserAsync(email);
        var book = await CreateBookAsync(client, title, pageCount);
        var addRes = await client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
        return (client, ub!);
    }

    // 11. Boş JSON 400 döner ve mevcut PageCount korunur
    [Fact]
    public async Task UpdatePageCount_JsonEmptyBody_Returns400_AndKeepsPageCount()
    {
        var (client, ub) = await CreateUserBookWithPageCountAsync(
            "ppc_json_empty@test.com", "Json Empty Book", 300);

        var putRes = await client.PutAsync(
            $"/api/library/{ub.Id}/page-count", JsonBody("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);

        var error = await putRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(error.TryGetProperty("message", out _));

        var after = await GetUserBookFromLibraryAsync(client, ub.Id);
        Assert.Equal(300, after.PageCount);
    }

    // 12. Yanlış alan adı 400 döner ve mevcut PageCount korunur
    [Fact]
    public async Task UpdatePageCount_JsonWrongPropertyName_Returns400_AndKeepsPageCount()
    {
        var (client, ub) = await CreateUserBookWithPageCountAsync(
            "ppc_json_wrongkey@test.com", "Json Wrong Key Book", 300);

        var putRes = await client.PutAsync(
            $"/api/library/{ub.Id}/page-count",
            JsonBody("{\"page_count\":250}"));
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);

        var after = await GetUserBookFromLibraryAsync(client, ub.Id);
        Assert.Equal(300, after.PageCount);
    }

    // 13. Açık null 200 döner, PageCount temizlenir, CurrentPage korunur
    [Fact]
    public async Task UpdatePageCount_JsonExplicitNull_Returns200_AndClearsPageCount()
    {
        var (client, ub) = await CreateUserBookWithPageCountAsync(
            "ppc_json_null@test.com", "Json Null Book", 300);

        var progressRes = await client.PutAsJsonAsync(
            $"/api/library/{ub.Id}/progress",
            new UpdateProgressRequest { CurrentPage = 120 });
        Assert.Equal(HttpStatusCode.OK, progressRes.StatusCode);

        var putRes = await client.PutAsync(
            $"/api/library/{ub.Id}/page-count",
            JsonBody("{\"pageCount\":null}"));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var updated = await putRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Null(updated!.PageCount);
        Assert.Equal(120, updated.CurrentPage);

        var after = await GetUserBookFromLibraryAsync(client, ub.Id);
        Assert.Null(after.PageCount);
        Assert.Equal(120, after.CurrentPage);
    }

    // 14. Normal değer 200 döner ve PageCount güncellenir
    [Fact]
    public async Task UpdatePageCount_JsonValue_Returns200_AndUpdatesPageCount()
    {
        var (client, ub) = await CreateUserBookWithPageCountAsync(
            "ppc_json_value@test.com", "Json Value Book", 300);

        var putRes = await client.PutAsync(
            $"/api/library/{ub.Id}/page-count",
            JsonBody("{\"pageCount\":250}"));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var updated = await putRes.Content.ReadFromJsonAsync<UserBookDto>();
        Assert.Equal(250, updated!.PageCount);

        var after = await GetUserBookFromLibraryAsync(client, ub.Id);
        Assert.Equal(250, after.PageCount);
    }
}
