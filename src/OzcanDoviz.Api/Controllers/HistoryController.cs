using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Core.Rates;

namespace OzcanDoviz.Api.Controllers;

/// <summary>
/// Sitenin Geçmiş Kurlar sayfası: günlük kapanış serisi (Harem Altın piyasa verisi, marjsız).
/// Panelde sayfa kapalıysa 404 döner.
/// </summary>
[ApiController]
[Route("api/v1/history")]
[AllowAnonymous]
public sealed class HistoryController(IRateHistoryService history, ISiteContentStore content) : ControllerBase
{
    [HttpGet("codes")]
    public async Task<ActionResult<object>> Codes(CancellationToken ct)
    {
        if (!(await content.GetAsync(ct)).Pages.History) return NotFound();
        return new { codes = history.Codes, days = HistoryCodes.AllowedDays };
    }

    [HttpGet("series")]
    public async Task<ActionResult<HistorySeries>> Series([FromQuery] string code, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!(await content.GetAsync(ct)).Pages.History) return NotFound();
        var series = await history.GetSeriesAsync(code, days, ct);
        if (series is null) return NotFound(new ProblemDetails { Title = "Bilinmeyen sembol." });
        Response.Headers.CacheControl = "public,max-age=300";
        return series;
    }
}
