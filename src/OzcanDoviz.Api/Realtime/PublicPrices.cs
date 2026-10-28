using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Api.Realtime;

/// <summary>
/// Dışarıya (site, mobil, SignalR) giden fiyat satırı. Ham sağlayıcı fiyatı bilinçli olarak yoktur:
/// kaynak fiyatı yayınlamak marjı açık eder.
/// </summary>
public sealed record PublicPriceItem(string Code, string Name, string Category, decimal Buy, decimal Sell, int Decimals, string Direction);

public sealed record PublicPriceMessage(string Type, string Provider, DateTime UpdatedUtc, bool Stale, IReadOnlyList<PublicPriceItem> Items);

public static class PublicPrices
{
    public static PublicPriceMessage From(BoardSnapshot snapshot, IReadOnlySet<string>? allowedCodes = null)
    {
        var items = snapshot.Items
            .Where(i => allowedCodes is null || allowedCodes.Count == 0 || allowedCodes.Contains(i.Code))
            .Select(i => new PublicPriceItem(i.Code, i.Name, i.Category, i.Buy, i.Sell, i.Decimals, i.Direction))
            .ToList();
        return new PublicPriceMessage("prices", snapshot.ProviderName, snapshot.UpdatedUtc, snapshot.Error is not null, items);
    }

    public static HashSet<string> ParseCodes(string? csv) =>
        (csv ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
