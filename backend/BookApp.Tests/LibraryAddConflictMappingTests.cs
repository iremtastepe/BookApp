
using System.Security.Claims;
using BookApp.API.Controllers;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Tests;

// 7.4-C.2 (R5): Kitap ekleme işleminin HTTP yanıt testleri.
public class LibraryAddConflictMappingTests
{
    private sealed class StubUserBookService : IUserBookService
    {
        private readonly Func<int, AddToLibraryRequest, Task<UserBookDto>> _add;

        public StubUserBookService(
            Func<int, AddToLibraryRequest, Task<UserBookDto>> add)
            => _add = add;

        public Task<UserBookDto> AddToLibraryAsync(
            int userId, AddToLibraryRequest request)
            => _add(userId, request);

        public Task<PagedResponse<UserBookDto>> GetLibraryAsync(
            int userId, LibraryQuery query)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateStatusAsync(
            int userId, int userBookId, UpdateUserBookStatusRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateReviewAndRatingAsync(
            int userId, int userBookId, UpdateReviewAndRatingRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> SetFavoriteAsync(
            int userId, int userBookId, SetFavoriteRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateProgressAsync(
            int userId, int userBookId, UpdateProgressRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateFinishedDateAsync(
            int userId, int userBookId, UpdateFinishedDateRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateStartedDateAsync(
            int userId, int userBookId, UpdateStartedDateRequest request)
            => throw new NotSupportedException();

        public Task<UserBookDto> ArchiveAsync(
            int userId, int userBookId)
            => throw new NotSupportedException();

        public Task<UserBookDto> RestoreAsync(
            int userId, int userBookId)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdatePageCountAsync(
            int userId, int userBookId,
            UpdateUserBookPageCountRequest request)
            => throw new NotSupportedException();
    }

    private static LibraryController CreateController(
        IUserBookService service)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "7") },
            "TestAuth");

        return new LibraryController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static string? MessageOf(object? value)
        => value?.GetType()
            .GetProperty("message")?
            .GetValue(value) as string;

    // 1. Başarılı kitap ekleme 201 döndürmeli.
    [Fact]
    public async Task AddToLibrary_WhenServiceSucceeds_Returns201WithCreatedRecord()
    {
        var dto = new UserBookDto
        {
            Id = 42,
            BookId = 5,
            Title = "T",
            Author = "A"
        };

        var controller = CreateController(
            new StubUserBookService(
                (_, _) => Task.FromResult(dto)));

        var result = await controller.AddToLibrary(
            new AddToLibraryRequest { BookId = 5 });

        var created = Assert.IsType<CreatedAtActionResult>(
            result.Result);

        Assert.Equal(
            StatusCodes.Status201Created,
            created.StatusCode);

        Assert.Same(dto, created.Value);
    }

    // 2. Kitap bulunamazsa 404 dönmeli.
    [Fact]
    public async Task AddToLibrary_WhenBookMissing_Returns404()
    {
        var controller = CreateController(
            new StubUserBookService(
                (_, _) => throw new KeyNotFoundException(
                    "Kitap bulunamadı.")));

        var result = await controller.AddToLibrary(
            new AddToLibraryRequest { BookId = 999 });

        var notFound = Assert.IsType<NotFoundObjectResult>(
            result.Result);

        Assert.Equal(
            "Kitap bulunamadı.",
            MessageOf(notFound.Value));
    }

    // 3. Aynı kitap zaten varsa 409 dönmeli.
    [Fact]
    public async Task AddToLibrary_WhenUserBookAlreadyExists_Returns409WithSameMessage()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(
            new UserBookAlreadyExistsException());

        var controller = CreateController(
            new StubUserBookService(
                (_, _) => throw new UserBookAlreadyExistsException()));

        var result = await controller.AddToLibrary(
            new AddToLibraryRequest { BookId = 5 });

        var conflict = Assert.IsType<ConflictObjectResult>(
            result.Result);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            conflict.StatusCode);

        Assert.Equal(
            UserBookAlreadyExistsException.DefaultMessage,
            MessageOf(conflict.Value));
    }

    // 4. Diğer veritabanı hataları yanlışlıkla 409'a dönüşmemeli.
    [Fact]
    public async Task AddToLibrary_WhenOtherDatabaseErrorOccurs_IsNotMappedTo409()
    {
        var controller = CreateController(
            new StubUserBookService(
                (_, _) => throw new DbUpdateException(
                    "başka bir veritabanı hatası",
                    new Exception("iç hata"))));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => controller.AddToLibrary(
                new AddToLibraryRequest { BookId = 5 }));
    }
}
