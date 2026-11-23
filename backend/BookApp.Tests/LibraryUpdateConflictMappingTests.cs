using System.Security.Claims;
using BookApp.API.Controllers;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using BookApp.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BookApp.Tests;

// 7.4-C.3 (R4): UserBook güncelleme endpoint'lerinin concurrency çakışmasında HTTP 409 yanıt testleri.
public class LibraryUpdateConflictMappingTests
{
    private sealed class ConcurrencyFaultingUserBookService : IUserBookService
    {
        public Task<UserBookDto> AddToLibraryAsync(int userId, AddToLibraryRequest request)
            => throw new NotSupportedException();

        public Task<PagedResponse<UserBookDto>> GetLibraryAsync(int userId, LibraryQuery query)
            => throw new NotSupportedException();

        public Task<UserBookDto> UpdateStatusAsync(int userId, int userBookId, UpdateUserBookStatusRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> UpdateReviewAndRatingAsync(int userId, int userBookId, UpdateReviewAndRatingRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> SetFavoriteAsync(int userId, int userBookId, SetFavoriteRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> UpdateProgressAsync(int userId, int userBookId, UpdateProgressRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> UpdateFinishedDateAsync(int userId, int userBookId, UpdateFinishedDateRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> UpdateStartedDateAsync(int userId, int userBookId, UpdateStartedDateRequest request)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> ArchiveAsync(int userId, int userBookId)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> RestoreAsync(int userId, int userBookId)
            => throw new UserBookConcurrencyException();

        public Task<UserBookDto> UpdatePageCountAsync(int userId, int userBookId, UpdateUserBookPageCountRequest request)
            => throw new UserBookConcurrencyException();
    }

    private static LibraryController CreateController(IUserBookService service)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "9") },
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
        => value?.GetType().GetProperty("message")?.GetValue(value) as string;

    [Fact]
    public async Task UpdateStatus_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdateStatus(1, new UpdateUserBookStatusRequest { Status = ReadingStatus.Reading });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task UpdateProgress_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdateProgress(1, new UpdateProgressRequest { CurrentPage = 10 });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task UpdateReviewAndRating_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdateReviewAndRating(1, new UpdateReviewAndRatingRequest { Rating = 4.5m });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task SetFavorite_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.SetFavorite(1, new SetFavoriteRequest { IsFavorite = true });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task UpdateFinishedDate_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdateFinishedDate(1, new UpdateFinishedDateRequest { FinishedDateUnknown = true });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task UpdateStartedDate_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdateStartedDate(1, new UpdateStartedDateRequest
        {
            StartedDate = new CompletionDateDto { Year = 2026, Month = 1, Day = 1 }
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task Archive_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.Archive(1);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task Restore_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.Restore(1);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }

    [Fact]
    public async Task UpdatePageCount_OnConcurrencyConflict_Returns409()
    {
        var controller = CreateController(new ConcurrencyFaultingUserBookService());
        var result = await controller.UpdatePageCount(1, new UpdateUserBookPageCountRequest { PageCount = 250 });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(UserBookConcurrencyException.DefaultMessage, MessageOf(conflict.Value));
    }
}
