using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

[ApiController]
[Route("api/v1/prices")]
[AllowAnonymous]
public sealed class PricesController(IPriceBoard board, IFeedSettingsStore settings) : ControllerBase
{
    /// <summary>Sitenin fiyat tablosu (marjlı). Kaynak fiyat içermez.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> Get(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var message = PublicPrices.From(board.Current);
        return new { shopName = s.ShopName, message.Provider, message.UpdatedUtc, message.Stale, message.Items };
    }

    /// <summary>
    /// Mobil istemci için REST karşılığı: <c>GET /api/v1/prices/client?key=odz_…</c> (veya <c>X-Api-Key</c> başlığı).
    /// İstemcinin izinli ürün filtresi uygulanır.
    /// </summary>
    [HttpGet("client")]
    public async Task<ActionResult<PublicPriceMessage>> ForClient([FromQuery] string? key, [FromServices] AppDbContext db, CancellationToken ct)
    {
        key = string.IsNullOrWhiteSpace(key) ? Request.Headers["X-Api-Key"].ToString() : key;
        if (string.IsNullOrWhiteSpace(key)) return Unauthorized();
        var hash = ApiKeys.Hash(key);
        var client = await db.ApiClients.AsNoTracking().FirstOrDefaultAsync(c => c.KeyHash == hash && c.IsActive, ct);
        if (client is null) return Unauthorized();
        return PublicPrices.From(board.Current, PublicPrices.ParseCodes(client.AllowedCodes));
    }
}
