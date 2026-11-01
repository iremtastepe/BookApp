using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public class AdminPageCountConsistencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminPageCountConsistencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, User User, string Token)> CreateUserWithRoleAsync(string email, string password, string? role)
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

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        loginResponse.EnsureSuccessStatusCode();
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        return (client, user, authResult.Token);
    }

    private async Task<UserBookDto> GetUserBookDirectAsync(int userBookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ub = await db.UserBooks.AsNoTracking().Include(x => x.Book).FirstAsync(x => x.Id == userBookId);
        return new UserBookDto
        {
            Id = ub.Id,
            BookId = ub.BookId,
            Status = ub.Status,
            CurrentPage = ub.CurrentPage,
            PageCount = ub.Book.PageCount,
            Rating = ub.Rating,
            Review = ub.Review,
            IsFavorite = ub.IsFavorite,
            StartedAt = ub.StartedAt,
            FinishedYear = ub.FinishedYear,
            FinishedMonth = ub.FinishedMonth,
            FinishedDay = ub.FinishedDay,
            DidNotFinishAt = ub.DidNotFinishAt
        };
    }

    private async Task<Book> GetBookDirectAsync(int bookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Books.AsNoTracking().FirstAsync(b => b.Id == bookId);
    }

    // 1. null -> 300: Read.CurrentPage 350 olsa bile 300'e eşitlenmeli.
    [Fact]
    public async Task Test1_PageCount_NullTo300_SyncsReadCurrentPageEvenIfHigher()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t1@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t1@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Null to 300 Book",
            Author = "Author T1",
            PageCount = null
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/progress", new UpdateProgressRequest { CurrentPage = 350 });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 300
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(300, freshUb.CurrentPage);
    }

    // 2. 300 -> 400: Bütün Read kayıtları güncellenmeli.
    [Fact]
    public async Task Test2_PageCount_300To400_UpdatesAllReadRecords()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t2@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t2@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "300 to 400 Book",
            Author = "Author T2",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 400
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(400, freshUb.CurrentPage);
    }

    // 3. 400 -> 250: Read kayıtları küçültülebilmeli.
    [Fact]
    public async Task Test3_PageCount_400To250_ReducesReadRecords()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t3@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t3@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "400 to 250 Book",
            Author = "Author T3",
            PageCount = 400
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 250
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(250, freshUb.CurrentPage);
    }

    // 4. Reading kullanıcısı sınırı aşıyorsa 409.
    [Fact]
    public async Task Test4_ReadingUserExceedsNewLimit_Returns409Conflict()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t4@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t4@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Reading Limit Book",
            Author = "Author T4",
            PageCount = 400
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 280 });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 250
        });
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    // 5. DidNotFinish kullanıcısı sınırı aşıyorsa 409.
    [Fact]
    public async Task Test5_DidNotFinishUserExceedsNewLimit_Returns409Conflict()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t5@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t5@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "DNF Limit Book",
            Author = "Author T5",
            PageCount = 400
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 280 });
        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.DidNotFinish
        });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 250
        });
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    // 6. Alt sınırla eşit değer kabul edilmeli.
    [Fact]
    public async Task Test6_PageCount_EqualToMaxCurrentPage_IsAccepted()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t6@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t6@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Equal Limit Book",
            Author = "Author T6",
            PageCount = 400
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 250 });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 250
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
    }

    // 7. 300 -> null: UserBook ilerlemeleri korunmalı.
    [Fact]
    public async Task Test7_PageCount_300ToNull_PreservesUserBookProgress()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t7@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t7@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "300 to Null Book",
            Author = "Author T7",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 150 });

        var jsonContent = new StringContent(
            "{\"title\":\"300 to Null Book\",\"author\":\"Author T7\",\"pageCount\":null}",
            Encoding.UTF8,
            "application/json"
        );
        var putRes = await adminClient.PutAsync($"/api/books/{book.Id}", jsonContent);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(150, freshUb.CurrentPage);

        var freshBook = await GetBookDirectAsync(book.Id);
        Assert.Null(freshBook.PageCount);
    }

    // 8. PUT isteğinde eksik pageCount -> 400.
    [Fact]
    public async Task Test8_PutWithoutPageCountField_Returns400BadRequest()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t8@test.com", "AdminPass123!", "Admin");

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Missing Field Book",
            Author = "Author T8",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var jsonContent = new StringContent(
            "{\"title\":\"Missing Field Book\",\"author\":\"Author T8\"}",
            Encoding.UTF8,
            "application/json"
        );
        var putRes = await adminClient.PutAsync($"/api/books/{book!.Id}", jsonContent);
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    // 9. PUT isteğinde açık null -> 200.
    [Fact]
    public async Task Test9_PutWithExplicitNullPageCount_Returns200Ok()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t9@test.com", "AdminPass123!", "Admin");

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Explicit Null Book",
            Author = "Author T9",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var jsonContent = new StringContent(
            "{\"title\":\"Explicit Null Book\",\"author\":\"Author T9\",\"pageCount\":null}",
            Encoding.UTF8,
            "application/json"
        );
        var putRes = await adminClient.PutAsync($"/api/books/{book!.Id}", jsonContent);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshBook = await GetBookDirectAsync(book.Id);
        Assert.Null(freshBook.PageCount);
    }

    // 10. PageCount değişmeyen PUT -> UserBook değişmemeli.
    [Fact]
    public async Task Test10_PageCountUnchanged_DoesNotTouchUserBooks()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t10@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t10@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Unchanged Title Book",
            Author = "Author T10",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.UserBooks.FirstAsync(x => x.Id == ub!.Id);
            record.Status = ReadingStatus.Read;
            record.CurrentPage = 150;
            await db.SaveChangesAsync();
        }

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = "Updated Title Only",
            Author = book.Author,
            PageCount = 300
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub!.Id);
        Assert.Equal(150, freshUb.CurrentPage);
    }

    // 11. Birden fazla kullanıcıya ait Read kayıtları birlikte eşitlenmeli.
    [Fact]
    public async Task Test11_MultipleReadUsers_AllSyncedTogether()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t11@test.com", "AdminPass123!", "Admin");
        var (user1Client, _, _) = await CreateUserWithRoleAsync("user1_t11@test.com", "UserPass123!", null);
        var (user2Client, _, _) = await CreateUserWithRoleAsync("user2_t11@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Multi Read Book",
            Author = "Author T11",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var ub1Res = await user1Client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub1 = await ub1Res.Content.ReadFromJsonAsync<UserBookDto>();
        await user1Client.PutAsJsonAsync($"/api/library/{ub1!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        var ub2Res = await user2Client.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book.Id });
        var ub2 = await ub2Res.Content.ReadFromJsonAsync<UserBookDto>();
        await user2Client.PutAsJsonAsync($"/api/library/{ub2!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 450
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb1 = await GetUserBookDirectAsync(ub1.Id);
        var freshUb2 = await GetUserBookDirectAsync(ub2.Id);
        Assert.Equal(450, freshUb1.CurrentPage);
        Assert.Equal(450, freshUb2.CurrentPage);
    }

    // 12. Başka BookId'ye ait kayıtlar korunmalı.
    [Fact]
    public async Task Test12_OtherBooksRecords_AreUntouched()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t12@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t12@test.com", "UserPass123!", null);

        var b1Res = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest { Title = "Book 1", Author = "A1", PageCount = 300 });
        var b1 = await b1Res.Content.ReadFromJsonAsync<BookResponse>();

        var b2Res = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest { Title = "Book 2", Author = "A2", PageCount = 500 });
        var b2 = await b2Res.Content.ReadFromJsonAsync<BookResponse>();

        var ub2Res = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = b2!.Id });
        var ub2 = await ub2Res.Content.ReadFromJsonAsync<UserBookDto>();
        await userClient.PutAsJsonAsync($"/api/library/{ub2!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });

        await adminClient.PutAsJsonAsync($"/api/books/{b1!.Id}", new UpdateBookRequest
        {
            Title = b1.Title,
            Author = b1.Author,
            PageCount = 400
        });

        var freshUb2 = await GetUserBookDirectAsync(ub2.Id);
        Assert.Equal(500, freshUb2.CurrentPage);
    }

    // 13. Rating, Review, Favorite ve tüm tarih alanları korunmalı.
    [Fact]
    public async Task Test13_RatingReviewFavoriteAndDates_ArePreserved()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t13@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t13@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Preserved Data Book",
            Author = "Author T13",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 5, Day = 20 }
        });

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 1, Day = 10 }
        });

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/review", new UpdateReviewAndRatingRequest
        {
            Rating = 4.5m,
            Review = "Harika bir kitap"
        });

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/favorite", new SetFavoriteRequest
        {
            IsFavorite = true
        });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = book.Title,
            Author = book.Author,
            PageCount = 350
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(350, freshUb.CurrentPage);
        Assert.Equal(4.5m, freshUb.Rating);
        Assert.Equal("Harika bir kitap", freshUb.Review);
        Assert.True(freshUb.IsFavorite);
        Assert.Equal(2024, freshUb.FinishedYear);
        Assert.Equal(5, freshUb.FinishedMonth);
        Assert.Equal(20, freshUb.FinishedDay);
        Assert.NotNull(freshUb.StartedAt);
    }

    // 14. 409 sonrasında Book ve UserBook verileri değişmemeli.
    [Fact]
    public async Task Test14_After409Conflict_BookAndUserBookAreUnchanged()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t14@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t14@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Conflict Rollback Book",
            Author = "Author T14",
            PageCount = 400
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/progress", new UpdateProgressRequest { CurrentPage = 280 });

        var putRes = await adminClient.PutAsJsonAsync($"/api/books/{book.Id}", new UpdateBookRequest
        {
            Title = "Modified Title Attempt",
            Author = "Modified Author Attempt",
            PageCount = 200
        });
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);

        var freshBook = await GetBookDirectAsync(book.Id);
        Assert.Equal(400, freshBook.PageCount);
        Assert.Equal("Conflict Rollback Book", freshBook.Title);

        var freshUb = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(280, freshUb.CurrentPage);
    }

    // 15. ISBN doğrulama ve çakışma davranışları korunmalı.
    [Fact]
    public async Task Test15_IsbnValidationAndConflict_Preserved()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t15@test.com", "AdminPass123!", "Admin");

        var b1Res = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "ISBN Book 1",
            Author = "A1",
            Isbn = "978-0-13-235088-4",
            PageCount = 300
        });
        Assert.Equal(HttpStatusCode.Created, b1Res.StatusCode);

        var b2Res = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "ISBN Book 2",
            Author = "A2",
            PageCount = 300
        });
        var b2 = await b2Res.Content.ReadFromJsonAsync<BookResponse>();

        var putConflictRes = await adminClient.PutAsJsonAsync($"/api/books/{b2!.Id}", new UpdateBookRequest
        {
            Title = b2.Title,
            Author = b2.Author,
            Isbn = "9780132350884",
            PageCount = 300
        });
        Assert.Equal(HttpStatusCode.Conflict, putConflictRes.StatusCode);

        var putBadIsbnRes = await adminClient.PutAsJsonAsync($"/api/books/{b2.Id}", new UpdateBookRequest
        {
            Title = b2.Title,
            Author = b2.Author,
            Isbn = "invalid-isbn-123",
            PageCount = 300
        });
        Assert.Equal(HttpStatusCode.BadRequest, putBadIsbnRes.StatusCode);
    }

    // 16. Yetkisiz kullanıcı için 401/403 davranışları korunmalı.
    [Fact]
    public async Task Test16_UnauthorizedAndForbidden_Preserved()
    {
        var unauthClient = _factory.CreateClient();
        var (normalClient, _, _) = await CreateUserWithRoleAsync("normal_t16@test.com", "UserPass123!", null);

        var unauthPut = await unauthClient.PutAsJsonAsync("/api/books/1", new UpdateBookRequest
        {
            Title = "Test",
            Author = "Test",
            PageCount = 100
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthPut.StatusCode);

        var forbiddenPut = await normalClient.PutAsJsonAsync("/api/books/1", new UpdateBookRequest
        {
            Title = "Test",
            Author = "Test",
            PageCount = 100
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPut.StatusCode);
    }

    // 17. Read -> Reading -> Read geçişleri 7.4-A ile uyumlu çalışmalı.
    [Fact]
    public async Task Test17_StatusTransitions_ReadToReadingToRead_CompatibleWith74A()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t17@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t17@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Transition Book",
            Author = "Author T17",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();

        await userClient.PutAsJsonAsync($"/api/library/{ub!.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        var r1 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(300, r1.CurrentPage);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Reading
        });
        var r2 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(0, r2.CurrentPage);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        var r3 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(300, r3.CurrentPage);
    }

    // 18. Repository değişikliklerinden sonra her kullanıcı yazma endpoint'inin gerçekten veritabanına kayıt yaptığı doğrulanmalı.
    [Fact]
    public async Task Test18_AllUserWriteEndpoints_PersistCorrectlyToDatabase()
    {
        var (adminClient, _, _) = await CreateUserWithRoleAsync("admin_t18@test.com", "AdminPass123!", "Admin");
        var (userClient, _, _) = await CreateUserWithRoleAsync("user_t18@test.com", "UserPass123!", null);

        var createBookRes = await adminClient.PostAsJsonAsync("/api/books", new CreateBookRequest
        {
            Title = "Persistence Book",
            Author = "Author T18",
            PageCount = 300
        });
        var book = await createBookRes.Content.ReadFromJsonAsync<BookResponse>();

        var addRes = await userClient.PostAsJsonAsync("/api/library", new AddToLibraryRequest { BookId = book!.Id });
        var ub = await addRes.Content.ReadFromJsonAsync<UserBookDto>();
        var dbUb1 = await GetUserBookDirectAsync(ub!.Id);
        Assert.NotNull(dbUb1);

        // Progress -> NotStarted'dan Reading'e geçer, sayfa 100 olur
        var progRes = await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/progress", new UpdateProgressRequest { CurrentPage = 100 });
        progRes.EnsureSuccessStatusCode();
        var dbUb3 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(100, dbUb3.CurrentPage);

        // StartedDate -> Reading durumunda düzenlenebilir
        var startRes = await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/started-date", new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2024, Month = 2, Day = 1 }
        });
        startRes.EnsureSuccessStatusCode();
        var dbUb2 = await GetUserBookDirectAsync(ub.Id);
        Assert.NotNull(dbUb2.StartedAt);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/status", new UpdateUserBookStatusRequest
        {
            Status = ReadingStatus.Read,
            FinishedDateUnknown = true
        });
        var dbUb4 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(ReadingStatus.Read, dbUb4.Status);
        Assert.Equal(300, dbUb4.CurrentPage);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/review", new UpdateReviewAndRatingRequest
        {
            Rating = 5.0m,
            Review = "Muazzam"
        });
        var dbUb5 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(5.0m, dbUb5.Rating);
        Assert.Equal("Muazzam", dbUb5.Review);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/favorite", new SetFavoriteRequest
        {
            IsFavorite = true
        });
        var dbUb6 = await GetUserBookDirectAsync(ub.Id);
        Assert.True(dbUb6.IsFavorite);

        await userClient.PutAsJsonAsync($"/api/library/{ub.Id}/finished-date", new UpdateFinishedDateRequest
        {
            FinishedDate = new CompletionDateDto { Year = 2024, Month = 3, Day = 10 }
        });
        var dbUb7 = await GetUserBookDirectAsync(ub.Id);
        Assert.Equal(2024, dbUb7.FinishedYear);
        Assert.Equal(3, dbUb7.FinishedMonth);
        Assert.Equal(10, dbUb7.FinishedDay);
    }
}
