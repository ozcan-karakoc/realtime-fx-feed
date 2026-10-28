using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

public sealed record MobileBroker(string Id, string Name);

/// <summary>Mobil uygulamanın tek seferde aldığı paket: aracı bilgisi, fiyatlar, içerik, canlı soket adresi.</summary>
public sealed record MobileBootstrap(
    MobileBroker Broker,
    PublicPriceMessage Prices,
    PublicContentMessage Content,
    string SocketUrl);

/// <summary>
/// Mobil servis: <c>/api/v1/mobile/{brokerId}</c>. İstemci anahtarı <c>X-Api-Key</c> başlığında (veya <c>?key=</c>).
/// brokerId, panelde Site İçeriği → Mobil bölümündeki aracı kimliğidir; eşleşmezse 404 döner.
/// </summary>
[ApiController]
[Route("api/v1/mobile/{brokerId}")]
[AllowAnonymous]
public sealed class MobileController(
    ISiteContentStore content,
    IFeedSettingsStore settings,
    IPriceBoard board,
    AppDbContext db) : ControllerBase
{
    /// <summary>Uygulama açılışında tek istek: fiyat + içerik + soket adresi.</summary>
    [HttpGet]
    public async Task<ActionResult<MobileBootstrap>> Bootstrap(string brokerId, CancellationToken ct)
    {
        var (error, client, site, shopName) = await ResolveAsync(brokerId, ct);
        if (error is not null) return error;

        var socket = (Request.IsHttps ? "wss" : "ws") + $"://{Request.Host}{MobileSocketHub.Path}";
        return new MobileBootstrap(
            new MobileBroker(site!.BrokerId, shopName!),
            PublicPrices.From(board.Current, PublicPrices.ParseCodes(client!.AllowedCodes)),
            PublicContent.From(site, shopName!, BaseUrl),
            socket);
    }

    [HttpGet("prices")]
    public async Task<ActionResult<PublicPriceMessage>> Prices(string brokerId, CancellationToken ct)
    {
        var (error, client, _, _) = await ResolveAsync(brokerId, ct);
        if (error is not null) return error;
        return PublicPrices.From(board.Current, PublicPrices.ParseCodes(client!.AllowedCodes));
    }

    [HttpGet("content")]
    public async Task<ActionResult<PublicContentMessage>> Content(string brokerId, CancellationToken ct)
    {
        var (error, _, site, shopName) = await ResolveAsync(brokerId, ct);
        if (error is not null) return error;
        return PublicContent.From(site!, shopName!, BaseUrl);
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    private async Task<(ActionResult? Error, ApiClient? Client, SiteContent? Site, string? ShopName)> ResolveAsync(string brokerId, CancellationToken ct)
    {
        var client = await ApiClientResolver.FindAsync(db, Request, ct);
        if (client is null)
            return (Unauthorized(new ProblemDetails { Title = "Geçersiz veya pasif istemci anahtarı." }), null, null, null);

        var site = await content.GetAsync(ct);
        if (!string.Equals(site.BrokerId, brokerId?.Trim(), StringComparison.OrdinalIgnoreCase))
            return (NotFound(new ProblemDetails { Title = "Aracı bulunamadı." }), null, null, null);

        client.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (null, client, site, (await settings.GetAsync(ct)).ShopName);
    }
}
