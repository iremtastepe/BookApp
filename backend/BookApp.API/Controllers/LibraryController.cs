using System.Security.Claims;
using BookApp.Application.DTOs.Books;
using BookApp.Application.DTOs.Library;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LibraryController : ControllerBase
{
    private readonly IUserBookService _userBookService;

    public LibraryController(IUserBookService userBookService)
    {
        _userBookService = userBookService;
    }

    private int GetUserId()
        => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<UserBookDto>> AddToLibrary(AddToLibraryRequest request)
    {
        try
        {
            var result = await _userBookService.AddToLibraryAsync(GetUserId(), request);
            return CreatedAtAction(nameof(AddToLibrary), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // [FromQuery] şart: [ApiController] varsayılan olarak karmaşık sınıfları gövdeden (body) okumaya çalışır.
    // Bu işaret "LibraryQuery'nin alanlarını URL'deki ?search=...&status=... parametrelerinden doldur" der.
    [HttpGet]
    public async Task<ActionResult<PagedResponse<UserBookDto>>> GetLibrary([FromQuery] LibraryQuery query)
    {
        try
        {
            var result = await _userBookService.GetLibraryAsync(GetUserId(), query);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/status")]
    public async Task<ActionResult<UserBookDto>> UpdateStatus(int id, UpdateUserBookStatusRequest request)
    {
        try
        {
            var result = await _userBookService.UpdateStatusAsync(GetUserId(), id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/progress")]
    public async Task<ActionResult<UserBookDto>> UpdateProgress(int id, UpdateProgressRequest request)
    {
        try
        {
            var result = await _userBookService.UpdateProgressAsync(GetUserId(), id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/review")]
    public async Task<ActionResult<UserBookDto>> UpdateReviewAndRating(int id, UpdateReviewAndRatingRequest request)
    {
        try
        {
            var result = await _userBookService.UpdateReviewAndRatingAsync(GetUserId(), id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/favorite")]
    public async Task<ActionResult<UserBookDto>> SetFavorite(int id, SetFavoriteRequest request)
    {
        try
        {
            var result = await _userBookService.SetFavoriteAsync(GetUserId(), id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Servis "Read olmayan kitap favoriye eklenemez" kuralını bu hatayla bildirir (409).
            // Bu yakalama olmadan hata yakalanmaz ve 500 dönerdi
            return Conflict(new { message = ex.Message });
        }
    }
}