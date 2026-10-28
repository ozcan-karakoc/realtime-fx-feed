using Microsoft.Extensions.Logging.Abstractions;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Infrastructure.Pricing;
using OzcanDoviz.Infrastructure.Providers;

namespace OzcanDoviz.Tests;

/// <summary>Kategori ve ürün bazında sağlayıcı seçimi: fiyat işçisi sahte sağlayıcılarla, ağsız çalıştırılır.</summary>
public class ProviderSelectionTests
{
    private sealed class FakeProvider(string key, decimal usd, decimal has, bool fail = false) : IPriceProvider
    {
        public string Key => key;
        public string DisplayName => key.ToUpperInvariant();
        public string Description => "";
        public ProviderTransport Transport => ProviderTransport.Http;
        public int Calls { get; private set; }

        public Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (fail) throw new InvalidOperationException("bağlantı yok");
            IReadOnlyList<PriceQuote> q =
            [
                new("USDTRY", usd, usd + 0.1m, DateTime.UtcNow),
                new("HASALTIN", has, has + 10m, DateTime.UtcNow),
            ];
            return Task.FromResult(q);
        }
    }

    private sealed class Margins(MarginDocument doc) : IMarginStore
    {
        public Task<MarginDocument> GetAsync(CancellationToken cancellationToken) => Task.FromResult(doc);
        public Task<MarginDocument> SaveAsync(MarginDocument document, string? user, CancellationToken cancellationToken) => Task.FromResult(document);
    }

    private sealed class Settings(FeedSettings s) : IFeedSettingsStore
    {
        public Task<FeedSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(s);
        public Task<FeedSettings> SaveAsync(FeedSettings settings, CancellationToken cancellationToken) => Task.FromResult(settings);
    }

    private static ProductRule Rule(string code, string category, string symbol, string? provider = null) => new()
    {
        Code = code, Name = code, Category = category, SourceSymbol = symbol, Provider = provider, Decimals = 2,
    };

    private static async Task<IReadOnlyList<BoardItem>> RunAsync(FeedSettings settings, params IPriceProvider[] providers)
    {
        var doc = new MarginDocument
        {
            Products = [Rule("USD", "doviz", "USDTRY"), Rule("HAS", "altin", "HASALTIN"), Rule("HAS_H", "altin", "HASALTIN", provider: "harem")],
        };
        var board = new PriceBoard();
        var worker = new PriceWorker(new ProviderRegistry(providers), new Settings(settings), new Margins(doc), board,
            [], new PriceRefreshSignal(), NullLogger<PriceWorker>.Instance);
        await worker.RunOnceAsync(default);
        return board.Current.Items;
    }

    [Fact]
    public void ProviderFor_prefers_product_then_category_then_general()
    {
        var s = new FeedSettings { ActiveProvider = "harem", CategoryProviders = { ["altin"] = "saglamoglu" } };
        Assert.Equal("harem", s.ProviderFor(Rule("USD", "doviz", "USDTRY")));
        Assert.Equal("saglamoglu", s.ProviderFor(Rule("HAS", "altin", "HASALTIN")));
        Assert.Equal("kitco", s.ProviderFor(Rule("HAS", "altin", "HASALTIN", provider: "kitco")));
    }

    [Fact]
    public async Task Gold_from_saglamoglu_currency_from_harem_with_product_override()
    {
        var harem = new FakeProvider("harem", usd: 41m, has: 6000m);
        var saglam = new FakeProvider("saglamoglu", usd: 99m, has: 6100m);
        var settings = new FeedSettings { ActiveProvider = "harem", FallbackProvider = null, CategoryProviders = { ["altin"] = "saglamoglu" } };

        var items = await RunAsync(settings, harem, saglam);

        Assert.Equal(41m, items.Single(i => i.Code == "USD").Buy);      // döviz: genel = harem
        Assert.Equal("harem", items.Single(i => i.Code == "USD").Provider);
        Assert.Equal(6100m, items.Single(i => i.Code == "HAS").Buy);    // altın kategorisi: sağlamoğlu
        Assert.Equal("saglamoglu", items.Single(i => i.Code == "HAS").Provider);
        Assert.Equal(6000m, items.Single(i => i.Code == "HAS_H").Buy);  // ürün seçimi kategoriyi ezer
    }

    [Fact]
    public async Task Failing_category_provider_falls_back()
    {
        var harem = new FakeProvider("harem", usd: 41m, has: 6000m);
        var saglam = new FakeProvider("saglamoglu", 0, 0, fail: true);
        var settings = new FeedSettings { ActiveProvider = "harem", FallbackProvider = "harem", CategoryProviders = { ["altin"] = "saglamoglu" } };

        var items = await RunAsync(settings, harem, saglam);

        var has = items.Single(i => i.Code == "HAS");
        Assert.Equal(6000m, has.Buy);
        Assert.Equal("harem", has.Provider);
        Assert.Equal(1, saglam.Calls);
    }

    [Fact]
    public async Task Unused_provider_is_not_called()
    {
        var harem = new FakeProvider("harem", 41m, 6000m);
        var kitco = new FakeProvider("kitco", 1m, 1m);
        await RunAsync(new FeedSettings { ActiveProvider = "harem", FallbackProvider = null }, harem, kitco);
        Assert.Equal(0, kitco.Calls);
    }
}
