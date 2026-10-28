using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Infrastructure.Storage;

/// <summary>Marjlar bu makinedeki <c>App_Data/margins.json</c> dosyasındadır.</summary>
internal sealed class MarginStore : IMarginStore
{
    private readonly JsonFileStore<MarginDocument> _file;

    public MarginStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<MarginStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<MarginDocument>(dir, "margins.json", options.Value.KeepHistory, DefaultMargins.Create, logger);
    }

    public Task<MarginDocument> GetAsync(CancellationToken cancellationToken) => _file.ReadAsync(cancellationToken);

    public Task<MarginDocument> SaveAsync(MarginDocument document, string? user, CancellationToken cancellationToken)
    {
        document.UpdatedUtc = DateTime.UtcNow;
        document.UpdatedBy = user;
        foreach (var p in document.Products)
        {
            p.Code = p.Code.Trim().ToUpperInvariant();
            p.SourceSymbol = p.SourceSymbol.Trim().ToUpperInvariant();
            p.Name = p.Name.Trim();
        }
        return _file.WriteAsync(document, cancellationToken);
    }
}

/// <summary>Sağlayıcı seçimi <c>App_Data/settings.json</c> dosyasındadır.</summary>
internal sealed class FeedSettingsStore : IFeedSettingsStore
{
    private readonly JsonFileStore<FeedSettings> _file;

    public FeedSettingsStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<FeedSettingsStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<FeedSettings>(dir, "settings.json", options.Value.KeepHistory, () => new FeedSettings(), logger);
    }

    public Task<FeedSettings> GetAsync(CancellationToken cancellationToken) => _file.ReadAsync(cancellationToken);

    public Task<FeedSettings> SaveAsync(FeedSettings settings, CancellationToken cancellationToken)
    {
        settings.PollSeconds = Math.Clamp(settings.PollSeconds, 2, 300);
        return _file.WriteAsync(settings, cancellationToken);
    }
}

/// <summary>
/// İlk açılıştaki ürün listesi. Ziynet katsayıları HAS altından hesaplanır (milyem/katsayı),
/// böylece yalnızca has fiyatı veren sağlayıcıda da tablo dolu gelir.
/// </summary>
internal static class DefaultMargins
{
    public static MarginDocument Create()
    {
        var order = 0;
        ProductRule Add(string code, string name, string category, string source, decimal buy, decimal sell, int decimals) =>
            new()
            {
                Code = code, Name = name, Category = category, SourceSymbol = source, Decimals = decimals, Order = ++order,
                Buy = new MarginSide { Mode = MarginMode.Add, Value = buy },
                Sell = new MarginSide { Mode = MarginMode.Add, Value = sell },
            };
        ProductRule Mul(string code, string name, decimal buy, decimal sell, int decimals = 2) =>
            new()
            {
                Code = code, Name = name, Category = "altin", SourceSymbol = "HASALTIN", Decimals = decimals, Order = ++order,
                Buy = new MarginSide { Mode = MarginMode.Multiply, Value = buy },
                Sell = new MarginSide { Mode = MarginMode.Multiply, Value = sell },
            };

        return new MarginDocument
        {
            Products =
            [
                Add("USD", "Amerikan Doları", "doviz", "USDTRY", 0, 0, 4),
                Add("EUR", "Euro", "doviz", "EURTRY", 0, 0, 4),
                Add("GBP", "İngiliz Sterlini", "doviz", "GBPTRY", 0, 0, 4),
                Add("CHF", "İsviçre Frangı", "doviz", "CHFTRY", 0, 0, 4),
                Add("SAR", "Suudi Riyali", "doviz", "SARTRY", 0, 0, 4),
                Add("EURUSD", "Euro / Dolar", "parite", "EURUSD", 0, 0, 4),
                Mul("HAS", "24 Ayar Has Altın", 1m, 1m),
                Add("GRAM", "Gram Altın", "altin", "GRAMALTIN", 0, 0, 2),
                Mul("AYAR22", "22 Ayar Milyem", 0.9120m, 0.9160m),
                Mul("AYAR18", "18 Ayar Milyem", 0.750m, 0.750m),
                Mul("AYAR14", "14 Ayar Milyem", 0.585m, 0.585m),
                Mul("CEYREK_YENI", "Yeni Çeyrek", 1.630m, 1.633m, 0),
                Mul("YARIM_YENI", "Yeni Yarım", 3.260m, 3.266m, 0),
                Mul("ZIYNET_YENI", "Yeni Ziynet (Tam)", 6.503m, 6.509m, 0),
                Mul("ATA_YENI", "Yeni Ata", 6.585m, 6.613m, 0),
                Mul("CEYREK_ESKI", "Eski Çeyrek", 1.600m, 1.6025m, 0),
                Mul("YARIM_ESKI", "Eski Yarım", 3.200m, 3.201m, 0),
                Add("ONS", "Ons Altın ($)", "maden", "XAUUSD", 0, 0, 2),
                Add("GUMUS", "Gümüş (gr)", "maden", "GUMUSTRY", 0, 0, 2),
            ],
        };
    }
}
