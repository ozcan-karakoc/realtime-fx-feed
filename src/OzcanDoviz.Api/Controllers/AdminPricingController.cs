using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Infrastructure.Persistence;
using OzcanDoviz.Infrastructure.Pricing;

namespace OzcanDoviz.Api.Controllers;

public sealed record FeedSettingsRequest(
    string ActiveProvider,
    string? FallbackProvider,
    int PollSeconds,
    string ShopName,
    Dictionary<string, string>? CategoryProviders = null,
    RateLockSettings? RateLock = null);
public sealed record RawSymbol(string Symbol, string? Name, decimal Buy, decimal Sell);

[ApiController]
[Route("api/v1/admin")]
[Authorize]
public sealed class AdminPricingController(
    IMarginStore margins,
    IFeedSettingsStore settings,
    IProviderRegistry providers,
    IPriceBoard board,
    PriceRefreshSignal refresh,
    AppDbContext db) : ControllerBase
{
    /// <summary>Marj kuralları (App_Data/margins.json).</summary>
    [HttpGet("margins")]
    public Task<MarginDocument> GetMargins(CancellationToken ct) => margins.GetAsync(ct);

    [HttpPut("margins")]
    public async Task<ActionResult<MarginDocument>> SaveMargins(MarginDocument document, CancellationToken ct)
    {
        document.Products ??= [];
        var errors = PriceCalculator.Validate(document.Products, key => providers.Find(key) is not null);
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["products"] = errors.ToArray() })
            {
                Title = "Marj kuralları kaydedilemedi.",
            });

        var saved = await margins.SaveAsync(document, User.Identity?.Name, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            User = User.Identity?.Name ?? "",
            Action = "margins.save",
            Detail = $"{saved.Products.Count} ürün",
        });
        await db.SaveChangesAsync(ct);
        refresh.Request();
        return saved;
    }

    /// <summary>Kullanılabilir sağlayıcılar ve seçili ayar.</summary>
    [HttpGet("providers")]
    public async Task<object> GetProviders(CancellationToken ct) => new
    {
        settings = await settings.GetAsync(ct),
        providers = providers.All.Select(p => new ProviderInfo(p.Key, p.DisplayName, p.Description, p.Transport.ToString())),
    };

    [HttpPut("providers")]
    public async Task<ActionResult<FeedSettings>> SaveProviders(FeedSettingsRequest request, CancellationToken ct)
    {
        if (providers.Find(request.ActiveProvider) is null)
            return BadRequest(new ProblemDetails { Title = $"Bilinmeyen sağlayıcı: {request.ActiveProvider}" });
        if (!string.IsNullOrWhiteSpace(request.FallbackProvider) && providers.Find(request.FallbackProvider) is null)
            return BadRequest(new ProblemDetails { Title = $"Bilinmeyen yedek sağlayıcı: {request.FallbackProvider}" });
        var unknown = (request.CategoryProviders ?? []).Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && providers.Find(v) is null);
        if (unknown is not null)
            return BadRequest(new ProblemDetails { Title = $"Bilinmeyen kategori sağlayıcısı: {unknown}" });

        // Kur sabitleme ayarı ayrı sayfadan da gönderilebilir; gönderilmezse mevcut ayar korunur.
        var current = await settings.GetAsync(ct);
        var saved = await settings.SaveAsync(new FeedSettings
        {
            ActiveProvider = request.ActiveProvider.Trim().ToLowerInvariant(),
            FallbackProvider = string.IsNullOrWhiteSpace(request.FallbackProvider) ? null : request.FallbackProvider.Trim().ToLowerInvariant(),
            PollSeconds = request.PollSeconds,
            ShopName = string.IsNullOrWhiteSpace(request.ShopName) ? "DEMO" : request.ShopName.Trim(),
            CategoryProviders = (request.CategoryProviders ?? current.CategoryProviders)
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key.Trim().ToLowerInvariant(), kv => kv.Value.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase),
            RateLock = request.RateLock ?? current.RateLock,
        }, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            User = User.Identity?.Name ?? "",
            Action = "providers.save",
            Detail = $"etkin={saved.ActiveProvider}, yedek={saved.FallbackProvider}, {saved.PollSeconds}s, " +
                     $"kategoriler={string.Join(';', saved.CategoryProviders.Select(kv => kv.Key + ":" + kv.Value))}, " +
                     $"sabitleme={(saved.RateLock.Enabled ? saved.RateLock.Minutes + "dk" : "kapalı")}",
        });
        await db.SaveChangesAsync(ct);
        refresh.Request();
        return saved;
    }

    /// <summary>Sağlayıcıyı seçmeden önce dener.</summary>
    [HttpPost("providers/{key}/test")]
    public async Task<ActionResult<ProviderTestResult>> Test(string key, CancellationToken ct)
    {
        var provider = providers.Find(key);
        if (provider is null) return NotFound();
        var watch = Stopwatch.StartNew();
        try
        {
            var quotes = await provider.GetQuotesAsync(ct);
            return new ProviderTestResult(true, $"{provider.DisplayName}: {quotes.Count} sembol, {watch.ElapsedMilliseconds} ms.", quotes.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ProviderTestResult(false, $"{provider.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>Etkin sağlayıcının ham sembolleri: marj ekranındaki "kaynak sembol" listesi.</summary>
    [HttpGet("symbols")]
    public async Task<ActionResult<IEnumerable<RawSymbol>>> Symbols([FromQuery] string? provider, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(provider) ? board.Current.Provider : provider;
        var p = providers.Find(key);
        if (p is null) return NotFound(new ProblemDetails { Title = $"Bilinmeyen sağlayıcı: {key}" });

        IReadOnlyList<Core.Prices.PriceQuote> quotes;
        if (board.RawByProvider.TryGetValue(p.Key, out var known) && known.Count > 0)
            quotes = known;
        else
        {
            // Henüz hiçbir ürün bu sağlayıcıyı kullanmıyor: seçim ekranı için bir kez okunur.
            try { quotes = await p.GetQuotesAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { quotes = []; }
        }
        return Ok(quotes.OrderBy(q => q.Symbol).Select(q => new RawSymbol(q.Symbol, q.Name, q.Buy, q.Sell)));
    }

    /// <summary>Panel özeti: kaynak + marjlı fiyat, hata, bağlı mobil soketler.</summary>
    [HttpGet("live")]
    public object Live([FromServices] MobileSocketHub sockets) => new
    {
        board.Current.Provider,
        board.Current.ProviderName,
        board.Current.UpdatedUtc,
        board.Current.Error,
        board.Current.Items,
        rawCount = board.RawByProvider.Values.Sum(q => q.Count),
        feeds = board.RawByProvider.Select(kv => new { provider = kv.Key, count = kv.Value.Count }),
        mobileConnections = sockets.Connections().Select(c => new { c.ClientId, c.ClientName, c.ConnectedUtc }),
    };
}
