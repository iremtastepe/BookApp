using System.Security.Claims;
using BookApp.Application.DTOs.Books;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BooksController : ControllerBase
{
    private readonly IBookService _bookService;

    public BooksController(IBookService bookService)
    {
        _bookService = bookService;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claim) || !int.TryParse(claim, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Geçerli bir kullanıcı kimliği bulunamadı.");
        }
        return userId;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<BookResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _bookService.GetBooksAsync(search, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookResponse>> GetById(int id)
    {
        var book = await _bookService.GetByIdAsync(id);
        return book is null ? NotFound() : Ok(book);
    }

    // 7.4-C.5: Kullanici basina dakikada 20 ISBN aramasi.
    [HttpGet("search-by-isbn")]
    [EnableRateLimiting("IsbnSearch")]
    public async Task<ActionResult<BookResponse>> SearchByIsbn(
        [FromQuery] string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return BadRequest(new { message = "ISBN boş olamaz." });

        int userId;
        try
        {
            userId = GetUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }

        try
        {
            var book = await _bookService.FindOrFetchByIsbnAsync(userId, isbn);
            return Ok(book);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ExternalServiceUnavailableException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "Kitap servisleri şu an yanıt vermiyor, " +
                              "lütfen biraz sonra tekrar deneyin."
                });
        }
    }

    // 7.4-C.5: Kullanici basina dakikada 10 kitap ekleme istegi.
    [HttpPost]
    [EnableRateLimiting("BookCreate")]
    public async Task<ActionResult<BookResponse>> Create(
        CreateBookRequest request)
    {
        int userId;
        try
        {
            userId = GetUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }

        try
        {
            var created = await _bookService.CreateAsync(userId, request);

            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BookResponse>> Update(
        int id,
        UpdateBookRequest request)
    {
        try
        {
            var updated = await _bookService.UpdateAsync(id, request);

            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _bookService.DeleteAsync(id);

            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
