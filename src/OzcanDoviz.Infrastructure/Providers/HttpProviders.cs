using System.Text;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Providers;

namespace OzcanDoviz.Infrastructure.Providers;

/// <summary>Herkese açık HTTP panoları. İstemci <see cref="HttpClientName"/> adıyla HttpClientFactory'den gelir.</summary>
internal abstract class HttpProvider(IHttpClientFactory http) : IPriceProvider
{
    public const string HttpClientName = "price-boards";

    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public ProviderTransport Transport => ProviderTransport.Http;

    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken)
    {
        var quotes = await FetchAsync(http.CreateClient(HttpClientName), cancellationToken);
        if (quotes.Count == 0)
            throw new InvalidOperationException($"{DisplayName} cevap verdi ama fiyat satırı yok.");
        return quotes;
    }

    protected abstract Task<IReadOnlyList<PriceQuote>> FetchAsync(HttpClient client, CancellationToken ct);

    protected async Task<string> ReadAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{DisplayName} {(int)response.StatusCode} döndü.");
        return body;
    }
}

/// <summary>
/// Sağlamoğlu TL panosu: ürün adları <c>/prices/products</c>, alış/satış <c>/prices/currentmarketproductprices</c>.
/// <c>labor-list</c> işçilik katsayısıdır, fiyat değildir; kullanılmaz.
/// </summary>
internal sealed class SaglamogluProvider(IHttpClientFactory http) : HttpProvider(http)
{
    private const string Root = "https://prdprc.saglamoglu.app/api/v1/prices/";

    public override string Key => "saglamoglu";
    public override string DisplayName => "Sağlamoğlu";
    public override string Description => "Kuyumcu panosu: has, gram, ziynet ve döviz.";

    protected override async Task<IReadOnlyList<PriceQuote>> FetchAsync(HttpClient client, CancellationToken ct)
    {
        var products = await ReadAsync(client, Root + "products", ct);
        var prices = await ReadAsync(client, Root + "currentmarketproductprices", ct);
        return BoardParse.Saglamoglu(products, prices, DateTime.UtcNow);
    }
}

internal sealed class AhlatciProvider(IHttpClientFactory http) : HttpProvider(http)
{
    public override string Key => "ahlatci";
    public override string DisplayName => "Ahlatçı Döviz";
    public override string Description => "Döviz bürosu panosu: döviz ve has altın.";

    protected override async Task<IReadOnlyList<PriceQuote>> FetchAsync(HttpClient client, CancellationToken ct) =>
        BoardParse.Ahlatci(await ReadAsync(client, "https://www.ahlatcidoviz.com.tr/static/currencies.json", ct), DateTime.UtcNow);
}

internal sealed class OzbeyProvider(IHttpClientFactory http) : HttpProvider(http)
{
    public override string Key => "ozbey";
    public override string DisplayName => "Özbey Fiziki";
    public override string Description => "Döviz ve kilo altın; kilo fiyatı gram has altına çevrilir.";

    protected override async Task<IReadOnlyList<PriceQuote>> FetchAsync(HttpClient client, CancellationToken ct) =>
        BoardParse.Ozbey(await ReadAsync(client, "https://www.ozbeyfiziki.com/mobil/data2.txt", ct), DateTime.UtcNow);
}

internal sealed class KitcoProvider(IHttpClientFactory http) : HttpProvider(http)
{
    private const string Query =
        """{"query":"query MetalQuote($symbol:String!,$currency:String!){ GetMetalQuoteV3(symbol:$symbol,currency:$currency){ symbol results { bid ask high low mid timestamp unit } } }","variables":{"symbol":"AU","currency":"USD"}}""";

    public override string Key => "kitco";
    public override string DisplayName => "Kitco (ons)";
    public override string Description => "Yalnızca XAUUSD ons altın. Yedek ons kaynağı olarak uygundur.";

    protected override async Task<IReadOnlyList<PriceQuote>> FetchAsync(HttpClient client, CancellationToken ct)
    {
        using var content = new StringContent(Query, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://kdb-gw.prod.kitco.com/", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Kitco {(int)response.StatusCode} döndü.");
        return BoardParse.Kitco(body, DateTime.UtcNow);
    }
}

/// <summary>Hazır sağlayıcılar + panelden eklenen özel sağlayıcılar (kaydedilince anında güncellenir).</summary>
public sealed class ProviderRegistry(IEnumerable<IPriceProvider> providers, CustomProviderService? custom = null) : IProviderRegistry
{
    private readonly IReadOnlyList<IPriceProvider> _builtIn = providers.ToList();

    public IReadOnlyList<IPriceProvider> BuiltIn => _builtIn;

    public IReadOnlyList<IPriceProvider> All => custom is null ? _builtIn : [.. _builtIn, .. custom.Providers];

    public IPriceProvider? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(p => p.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
}
