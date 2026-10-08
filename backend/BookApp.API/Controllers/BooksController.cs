using BookApp.Application.DTOs.Books;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("search-by-isbn")]
    public async Task<ActionResult<BookResponse>> SearchByIsbn([FromQuery] string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return BadRequest(new { message = "ISBN boş olamaz." });

        try
        {
            var book = await _bookService.FindOrFetchByIsbnAsync(isbn);
            return Ok(book);
        }
        catch (ArgumentException ex) // geçersiz ISBN -> 400
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex) // iki kaynakta da yok -> 404
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ExternalServiceUnavailableException) // dış servisler çalışmıyor -> 503
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Kitap servisleri şu an yanıt vermiyor, lütfen biraz sonra tekrar deneyin."
            });
        }
    }

    [HttpPost]
    public async Task<ActionResult<BookResponse>> Create(CreateBookRequest request)
    {
        try
        {
            var created = await _bookService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex) // geçersiz ISBN -> 400
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BookResponse>> Update(int id, UpdateBookRequest request)
    {
        try
        {
            var updated = await _bookService.UpdateAsync(id, request);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex) // geçersiz ISBN -> 400
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
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