using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Rates;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Infrastructure.Rates;

public sealed class HistoryCacheDocument
{
    /// <summary>"tip|yyyy-MM-dd" → o günün tüm satırları. Geçmiş günler değişmez, kalıcı saklanır.</summary>
    public Dictionary<string, List<HaremAltinRateDto>> Days { get; set; } = [];
}

/// <summary>
/// Günlük kapanış serisi. Her gün için Harem'e bir istek gider; geçmiş günler <c>App_Data/history-cache.json</c>
/// dosyasında tutulur, bugün 10 dakika bellekte. Aynı anda en fazla 4 istek gönderilir. Hafta sonu Harem önceki
/// iş gününün kaydını döndürür; aynı kayıt günü bir kez sayılır.
/// </summary>
internal sealed class RateHistoryService : IRateHistoryService
{
    private static readonly TimeSpan TodayTtl = TimeSpan.FromMinutes(10);
    private const int MaxCachedDays = 2000;

    // Typed HttpClient kısa ömürlüdür; tekil servis her seride yeni bir örnek alır (bağlantı havuzu yenilenebilsin).
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RateHistoryService> _logger;
    private readonly JsonFileStore<HistoryCacheDocument> _file;
    private readonly SemaphoreSlim _parallel = new(4, 4);
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly ConcurrentDictionary<string, (DateTime At, List<HaremAltinRateDto> Rows)> _today = new();

    public RateHistoryService(IServiceScopeFactory scopes, IOptions<StorageOptions> options, IHostEnvironment env, ILogger<RateHistoryService> logger)
    {
        _scopes = scopes;
        _logger = logger;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<HistoryCacheDocument>(dir, "history-cache.json", keepHistory: 0, () => new HistoryCacheDocument(), logger);
    }

    public IReadOnlyList<HistoryCode> Codes => HistoryCodes.All;

    public async Task<HistorySeries?> GetSeriesAsync(string code, int days, CancellationToken cancellationToken)
    {
        var meta = HistoryCodes.All.FirstOrDefault(c => c.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (meta is null) return null;
        days = HistoryCodes.AllowedDays.Contains(days) ? days : 30;
        var type = HistoryCodes.TypeOf(meta);

        var today = DateTime.Today;
        var dates = Enumerable.Range(0, days).Select(i => today.AddDays(-i)).Reverse().ToList();
        var cache = await _file.ReadAsync(cancellationToken);
        using var scope = _scopes.CreateScope();
        var harem = scope.ServiceProvider.GetRequiredService<IHaremAltinService>();
        var fetched = new ConcurrentDictionary<string, List<HaremAltinRateDto>>();
        var missing = 0;

        await Task.WhenAll(dates.Select(async date =>
        {
            var rows = await GetDayAsync(harem, cache, type, date, today, fetched, cancellationToken);
            if (rows is null) Interlocked.Increment(ref missing);
        }));

        if (!fetched.IsEmpty) await SaveAsync(fetched, cancellationToken);

        // Her istek gününün satırını al; aynı kayıt günü (hafta sonu tekrarı) bir kez.
        var points = new SortedDictionary<DateOnly, HistoryPoint>();
        foreach (var date in dates)
        {
            var rows = Lookup(cache, fetched, type, date, today);
            var row = rows?.FirstOrDefault(r => r.Code.Equals(meta.Code, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.Buy <= 0 || row.Sell <= 0) continue;
            var day = DateOnly.FromDateTime(row.RecordDate);
            points[day] = new HistoryPoint(day, row.Buy, row.Sell);
        }
        return new HistorySeries(meta.Code, meta.Name, meta.Decimals, points.Values.ToList(), missing);
    }

    private static string Key(HaremHistoryType type, DateTime date) =>
        $"{(int)type}|{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private List<HaremAltinRateDto>? Lookup(HistoryCacheDocument cache, ConcurrentDictionary<string, List<HaremAltinRateDto>> fetched,
        HaremHistoryType type, DateTime date, DateTime today)
    {
        var key = Key(type, date);
        if (date == today) return _today.TryGetValue(key, out var t) ? t.Rows : null;
        if (fetched.TryGetValue(key, out var f)) return f;
        return cache.Days.TryGetValue(key, out var c) ? c : null;
    }

    private async Task<List<HaremAltinRateDto>?> GetDayAsync(IHaremAltinService harem, HistoryCacheDocument cache, HaremHistoryType type, DateTime date, DateTime today,
        ConcurrentDictionary<string, List<HaremAltinRateDto>> fetched, CancellationToken ct)
    {
        var key = Key(type, date);
        if (date == today)
        {
            if (_today.TryGetValue(key, out var t) && DateTime.UtcNow - t.At < TodayTtl) return t.Rows;
        }
        else if (cache.Days.TryGetValue(key, out var cached))
            return cached;

        await _parallel.WaitAsync(ct);
        try
        {
            var rows = await harem.GetHistoricalRatesAsync(date, type, ct);
            if (date == today) _today[key] = (DateTime.UtcNow, rows);
            else fetched[key] = rows;
            return rows;
        }
        catch (HaremAltinException ex)
        {
            _logger.LogWarning("Geçmiş kur alınamadı {Date:yyyy-MM-dd} tip={Type}: {Error}", date, (int)type, ex.Message);
            return null;
        }
        finally
        {
            _parallel.Release();
        }
    }

    private async Task SaveAsync(ConcurrentDictionary<string, List<HaremAltinRateDto>> fetched, CancellationToken ct)
    {
        await _write.WaitAsync(ct);
        try
        {
            var doc = await _file.ReadAsync(ct);
            var days = new Dictionary<string, List<HaremAltinRateDto>>(doc.Days);
            foreach (var (k, v) in fetched) days[k] = v;
            // Çok eski günler budanır (anahtar tarih sırasına göre).
            if (days.Count > MaxCachedDays)
                days = days.OrderByDescending(kv => kv.Key[(kv.Key.IndexOf('|') + 1)..]).Take(MaxCachedDays).ToDictionary();
            await _file.WriteAsync(new HistoryCacheDocument { Days = days }, ct);
        }
        finally
        {
            _write.Release();
        }
    }
}
