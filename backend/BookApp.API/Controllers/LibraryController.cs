using System.Security.Claims;
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
}