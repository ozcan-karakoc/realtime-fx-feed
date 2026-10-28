using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;

namespace OzcanDoviz.Infrastructure.Pricing;

internal sealed class PriceBoard : IPriceBoard
{
    private volatile BoardSnapshot _current = BoardSnapshot.Empty;
    private volatile IReadOnlyList<PriceQuote> _raw = [];

    public BoardSnapshot Current => _current;
    public IReadOnlyList<PriceQuote> RawQuotes => _raw;

    public void Publish(BoardSnapshot snapshot, IReadOnlyList<PriceQuote> raw)
    {
        _raw = raw;
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
/// Etkin sağlayıcıdan fiyat çeker, JSON'daki marjları uygular, tablo değiştiyse yayınlar.
/// Socket sağlayıcı (bellekte) her saniye, HTTP sağlayıcı <see cref="FeedSettings.PollSeconds"/> aralıkla okunur.
/// Etkin sağlayıcı hata verirse yedek sağlayıcıya geçilir; ikisi de düşerse son tablo hata notuyla korunur.
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

    private IReadOnlyList<PriceQuote> _raw = [];
    private IPriceProvider? _rawFrom;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private string? _lastError;

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
            if (forced) _lastFetchUtc = DateTime.MinValue;
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        var active = registry.Find(settings.ActiveProvider) ?? registry.All[0];
        var fallback = registry.Find(settings.FallbackProvider);
        if (fallback == active) fallback = null;

        var interval = active.Transport == ProviderTransport.Socket ? Tick : TimeSpan.FromSeconds(settings.PollSeconds);
        var due = _rawFrom != active || DateTime.UtcNow - _lastFetchUtc >= interval - TimeSpan.FromMilliseconds(100);
        if (due) await FetchAsync(active, fallback, ct);

        var margins = await marginStore.GetAsync(ct);
        var previous = board.Current.Items.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        var items = PriceCalculator.Apply(margins.Products, _raw, previous);
        var source = _rawFrom ?? active;
        var snapshot = new BoardSnapshot(source.Key, source.DisplayName, DateTime.UtcNow,
            items.Count > 0 ? items : board.Current.Items, _lastError);

        if (!Changed(board.Current, snapshot)) return;
        board.Publish(snapshot, _raw);
        foreach (var b in broadcasters)
        {
            try { await b.BroadcastAsync(snapshot, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Yayın başarısız: {Broadcaster}", b.GetType().Name);
            }
        }
    }

    private async Task FetchAsync(IPriceProvider active, IPriceProvider? fallback, CancellationToken ct)
    {
        _lastFetchUtc = DateTime.UtcNow;
        try
        {
            _raw = await active.GetQuotesAsync(ct);
            _rawFrom = active;
            _lastError = null;
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _lastError = $"{active.DisplayName}: {ex.Message}";
            logger.LogWarning("Etkin sağlayıcı hata verdi: {Error}", _lastError);
        }

        if (fallback is null) return;
        try
        {
            _raw = await fallback.GetQuotesAsync(ct);
            _rawFrom = fallback;
            _lastError += $" — yedek {fallback.DisplayName} kullanılıyor.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _lastError += $" Yedek {fallback.DisplayName}: {ex.Message}";
            logger.LogWarning(ex, "Yedek sağlayıcı da hata verdi");
        }
    }

    private static bool Changed(BoardSnapshot old, BoardSnapshot now)
    {
        if (old.Provider != now.Provider || old.Error != now.Error || old.Items.Count != now.Items.Count) return true;
        for (var i = 0; i < now.Items.Count; i++)
        {
            var a = old.Items[i];
            var b = now.Items[i];
            if (a.Code != b.Code || a.Buy != b.Buy || a.Sell != b.Sell || a.Name != b.Name || a.Decimals != b.Decimals)
                return true;
        }
        return false;
    }
}
