using System.Security.Claims;
using BookApp.Application.DTOs.Goals;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Tüm endpoint'ler JWT kimlik doğrulaması gerektirir (401 Unauthorized)
public class ReadingGoalsController : ControllerBase
{
    private readonly IReadingGoalService _readingGoalService;

    // Dependency Injection: IReadingGoalService nesnesi constructor üzerinden enjekte edilir
    public ReadingGoalsController(IReadingGoalService readingGoalService)
    {
        _readingGoalService = readingGoalService;
    }

    // JWT token'ından giriş yapan kullanıcının Id'sini güvenli biçimde okur
    private int GetUserId()
        => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Belirtilen takvim yılı için okuma hedefi oluşturur veya mevcutsa günceller (Upsert).
    /// PUT /api/ReadingGoals/2026
    /// </summary>
    [HttpPut("{year}")]
    public async Task<ActionResult<AnnualReadingGoalDto>> CreateOrUpdateGoal(int year, CreateOrUpdateReadingGoalRequest request)
    {
        try
        {
            var result = await _readingGoalService.CreateOrUpdateGoalAsync(GetUserId(), year, request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Belirtilen takvim yılının hedefini, okunan kitap sayısını ve ilerleme yüzdesini getirir.
    /// Hedef yoksa sistem hata vermez; hedef alanları null olarak istatistik döner.
    /// GET /api/ReadingGoals/2026
    /// </summary>
    [HttpGet("{year}")]
    public async Task<ActionResult<AnnualReadingGoalDto>> GetAnnualGoal(int year)
    {
        try
        {
            var result = await _readingGoalService.GetAnnualGoalAsync(GetUserId(), year);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Belirtilen yıl ve ayda okunan kitapların listesini ve toplam sayısını sayfalama desteğiyle getirir ("Bu Ay Okuduklarım").
    /// GET /api/ReadingGoals/monthly?year=2026&month=10&page=1&pageSize=20
    /// </summary>
    [HttpGet("monthly")]
    public async Task<ActionResult<MonthlyReadSummaryDto>> GetMonthlySummary(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _readingGoalService.GetMonthlySummaryAsync(GetUserId(), year, month, page, pageSize);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
