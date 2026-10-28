using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;

namespace OzcanDoviz.Infrastructure.Pricing;

internal sealed class PriceBoard : IPriceBoard
{
    private volatile BoardSnapshot _current = BoardSnapshot.Empty;
    private volatile IReadOnlyDictionary<string, IReadOnlyList<PriceQuote>> _raw =
        new Dictionary<string, IReadOnlyList<PriceQuote>>();

    public BoardSnapshot Current => _current;
    public IReadOnlyList<PriceQuote> RawQuotes => _raw.TryGetValue(_current.Provider, out var q) ? q : [];
    public IReadOnlyDictionary<string, IReadOnlyList<PriceQuote>> RawByProvider => _raw;

    public void Publish(BoardSnapshot snapshot, IReadOnlyDictionary<string, IReadOnlyList<PriceQuote>> rawByProvider)
    {
        _raw = rawByProvider;
        _current = snapshot;
    }
}

/// <summary>Marj veya sağlayıcı değişince işçinin beklemeden yeni tablo üretmesini sağlar.</summary>
public sealed class PriceRefreshSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* zaten bekleyen bir istek var */ }
    }

    internal Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Gereken her sağlayıcıdan (genel, yedek, kategori ve ürün seçimleri) fiyat çeker, JSON'daki marjları uygular,
/// tablo değiştiyse yayınlar. Socket sağlayıcı (bellekte) her saniye, HTTP sağlayıcı <see cref="FeedSettings.PollSeconds"/>
/// aralıkla okunur. Bir ürünün sağlayıcısı hata verirse o ürün yedek sağlayıcıdan hesaplanır.
/// </summary>
internal sealed class PriceWorker(
    IProviderRegistry registry,
    IFeedSettingsStore settingsStore,
    IMarginStore marginStore,
    IPriceBoard board,
    IEnumerable<IPriceBroadcaster> broadcasters,
    PriceRefreshSignal refresh,
    ILogger<PriceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private sealed class FeedState
    {
        public IReadOnlyList<PriceQuote> Quotes = [];
        public Dictionary<string, PriceQuote> Index = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastFetchUtc = DateTime.MinValue;
        public string? Error;
    }

    private readonly Dictionary<string, FeedState> _feeds = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var forced = false;
            try
            {
                await RunOnceAsync(stoppingToken);
                forced = await refresh.WaitAsync(Tick, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fiyat turu başarısız");
                try { await Task.Delay(Tick, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
            if (forced)
                foreach (var f in _feeds.Values) f.LastFetchUtc = DateTime.MinValue;
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        var margins = await marginStore.GetAsync(ct);
        var active = registry.Find(settings.ActiveProvider) ?? registry.All[0];
        var fallback = registry.Find(settings.FallbackProvider);
        if (fallback == active) fallback = null;

        // Okunacak sağlayıcılar: genel + yedek + kategori/ürün seçimleri.
        var needed = new List<IPriceProvider> { active };
        if (fallback is not null) needed.Add(fallback);
        foreach (var rule in margins.Products.Where(p => p.Visible))
        {
            var p = registry.Find(settings.ProviderFor(rule));
            if (p is not null && !needed.Contains(p)) needed.Add(p);
        }

        foreach (var provider in needed)
            await FetchIfDueAsync(provider, settings.PollSeconds, ct);

        var previous = board.Current.Items.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        var items = PriceCalculator.Apply(margins.Products, rule =>
        {
            // Ürünün sağlayıcısı düştüyse ya da sembolü yoksa sırayla yedek, sonra genel sağlayıcı denenir.
            var own = (registry.Find(settings.ProviderFor(rule)) ?? active).Key;
            foreach (var key in new[] { own, fallback?.Key, active.Key })
            {
                if (key is not null && _feeds.TryGetValue(key, out var s) && s.Index.ContainsKey(rule.SourceSymbol))
                    return (s.Index, key);
            }
            return (null, own);
        }, previous);

        var used = items.Select(i => i.Provider).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => registry.Find(k)?.DisplayName ?? k).ToList();
        var errors = needed.Select(p => _feeds.TryGetValue(p.Key, out var s) ? s.Error : null).Where(e => e is not null).ToList();
        var snapshot = new BoardSnapshot(
            active.Key,
            used.Count > 0 ? string.Join(" + ", used) : active.DisplayName,
            DateTime.UtcNow,
            items.Count > 0 ? items : board.Current.Items,
            errors.Count > 0 ? string.Join(" · ", errors) : null);

        if (!Changed(board.Current, snapshot)) return;
        board.Publish(snapshot, _feeds.ToDictionary(kv => kv.Key, kv => kv.Value.Quotes, StringComparer.OrdinalIgnoreCase));
        foreach (var b in broadcasters)
        {
            try { await b.BroadcastAsync(snapshot, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Yayın başarısız: {Broadcaster}", b.GetType().Name);
            }
        }
    }

    private async Task FetchIfDueAsync(IPriceProvider provider, int pollSeconds, CancellationToken ct)
    {
        if (!_feeds.TryGetValue(provider.Key, out var state))
            _feeds[provider.Key] = state = new FeedState();

        var interval = provider.Transport == ProviderTransport.Socket ? Tick : TimeSpan.FromSeconds(pollSeconds);
        if (DateTime.UtcNow - state.LastFetchUtc < interval - TimeSpan.FromMilliseconds(100)) return;

        state.LastFetchUtc = DateTime.UtcNow;
        try
        {
            state.Quotes = await provider.GetQuotesAsync(ct);
            state.Index = PriceCalculator.Index(state.Quotes);
            state.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Son iyi fiyat korunur; hata panelde gösterilir.
            state.Error = $"{provider.DisplayName}: {ex.Message}";
            logger.LogWarning("Sağlayıcı hata verdi: {Error}", state.Error);
        }
    }

    private static bool Changed(BoardSnapshot old, BoardSnapshot now)
    {
        if (old.Provider != now.Provider || old.ProviderName != now.ProviderName || old.Error != now.Error
            || old.Items.Count != now.Items.Count) return true;
        for (var i = 0; i < now.Items.Count; i++)
        {
            var a = old.Items[i];
            var b = now.Items[i];
            if (a.Code != b.Code || a.Buy != b.Buy || a.Sell != b.Sell || a.Name != b.Name
                || a.Decimals != b.Decimals || a.Provider != b.Provider)
                return true;
        }
        return false;
    }
}
