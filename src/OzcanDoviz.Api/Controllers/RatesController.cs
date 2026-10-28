using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Core.Rates;

namespace OzcanDoviz.Api.Controllers;

[ApiController]
[Route("api/v1/rates")]
[Authorize]
public sealed class RatesController(IHaremAltinService harem, ILogger<RatesController> logger) : ControllerBase
{
    /// <summary>
    /// Harem Altın geçmiş kurları. <c>date</c> ISO (2026-09-23) alınır, Harem'e dd-MM-yyyy gönderilir.
    /// <c>code</c> verilirse yalnızca o kur döner (ör. ALTIN, CEYREK_YENI, AYAR22).
    /// Dönen <c>recordDate</c> Harem'in kayıt zamanıdır; istek tarihinden bir gün önce olabilir.
    /// </summary>
    [HttpGet("harem/history")]
    [ProducesResponseType<List<HaremAltinRateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<List<HaremAltinRateDto>>> HaremHistory(
        [FromQuery] DateTime? date,
        [FromQuery] string? code,
        CancellationToken cancellationToken)
    {
        if (date is null)
            return BadRequest(new ProblemDetails { Title = "date zorunlu (ör. 2026-09-23)." });
        if (date.Value.Date > DateTime.Today)
            return BadRequest(new ProblemDetails { Title = "Gelecek tarih için kur yok." });

        try
        {
            var rates = await harem.GetHistoricalRatesAsync(date.Value.Date, cancellationToken);
            if (string.IsNullOrWhiteSpace(code)) return rates;

            var filtered = rates.Where(r => r.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return filtered.Count == 0
                ? NotFound(new ProblemDetails { Title = $"{code.Trim().ToUpperInvariant()} kodu bu tarihte bulunamadı." })
                : filtered;
        }
        catch (HaremAltinException ex)
        {
            logger.LogWarning(ex, "Harem geçmiş kur alınamadı: {Date:yyyy-MM-dd}", date);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = ex.Message,
                Status = StatusCodes.Status502BadGateway,
                Extensions = { ["upstreamStatus"] = ex.UpstreamStatus },
            });
        }
    }
}
