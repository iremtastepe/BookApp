using System.Security.Claims;
using BookApp.Application.DTOs.Statistics;
using BookApp.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Tüm endpoint'ler JWT kimlik doğrulaması gerektirir (401 Unauthorized)
public class StatisticsController : ControllerBase
{
    private readonly IStatisticsService _statisticsService;

    // Dependency Injection: IStatisticsService nesnesi constructor üzerinden enjekte edilir
    public StatisticsController(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    // JWT token'ından giriş yapan kullanıcının Id'sini okur.
    // Kullanıcı Id'si istekten değil token'dan gelir: kimse başkasının istatistiğini isteyemez.
    private int GetUserId()
        => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Genel özet: toplam kitap, durum dağılımı, favori sayısı, en çok okunan 5 yazar ve tür,
    /// en yüksek puanlı 5 kitap ve puan dağılımı.
    /// GET /api/Statistics/overview
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewStatisticsDto>> GetOverview()
    {
        // Parametre almadığı için doğrulama hatası olamaz, try/catch gerekmez
        var result = await _statisticsService.GetOverviewAsync(GetUserId());
        return Ok(result);
    }

    /// <summary>
    /// Seçilen ayın analizi (tamamlanma tarihine göre).
    /// GET /api/Statistics/monthly?year=2026&month=10
    /// </summary>
    [HttpGet("monthly")]
    public async Task<ActionResult<MonthlyStatisticsDto>> GetMonthly([FromQuery] int year, [FromQuery] int month)
    {
        try
        {
            var result = await _statisticsService.GetMonthlyAsync(GetUserId(), year, month);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            // Geçersiz veya eksik yıl/ay: parametre gelmezse int 0 olur ve Service bunu reddeder
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Yıllara göre karşılaştırma ve seçilen yılın 12 aylık dağılımı.
    /// GET /api/Statistics/yearly?year=2026
    /// </summary>
    [HttpGet("yearly")]
    public async Task<ActionResult<YearlyStatisticsDto>> GetYearly([FromQuery] int year)
    {
        try
        {
            var result = await _statisticsService.GetYearlyAsync(GetUserId(), year);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}