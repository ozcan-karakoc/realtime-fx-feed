using OzcanDoviz.Core.Prices;

namespace OzcanDoviz.Core.Pricing;

public sealed record BoardItem(
    string Code,
    string Name,
    string Category,
    decimal Buy,
    decimal Sell,
    int Decimals,
    string Direction,
    decimal SourceBuy,
    decimal SourceSell,
    string SourceSymbol);

/// <summary>Ham fiyata marj kurallarını uygular. Saf fonksiyon: dosya, saat, ağ yok.</summary>
public static class PriceCalculator
{
    public static IReadOnlyList<BoardItem> Apply(
        IEnumerable<ProductRule> rules,
        IReadOnlyCollection<PriceQuote> quotes,
        IReadOnlyDictionary<string, BoardItem>? previous = null)
    {
        var bySymbol = new Dictionary<string, PriceQuote>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in quotes) bySymbol.TryAdd(q.Symbol, q);

        var list = new List<BoardItem>();
        foreach (var rule in rules.Where(r => r.Visible).OrderBy(r => r.Order).ThenBy(r => r.Name))
        {
            if (!bySymbol.TryGetValue(rule.SourceSymbol, out var src)) continue;
            var buy = Round(Adjust(src.Buy, rule.Buy), rule.Decimals);
            var sell = Round(Adjust(src.Sell, rule.Sell), rule.Decimals);
            if (buy <= 0 || sell <= 0) continue;

            // Yön, son değişimi korur: fiyat aynı kalınca ok kaybolmaz.
            var direction = "same";
            if (previous is not null && previous.TryGetValue(rule.Code, out var old))
                direction = sell > old.Sell ? "up" : sell < old.Sell ? "down" : old.Direction;

            list.Add(new BoardItem(rule.Code, rule.Name, rule.Category, buy, sell, rule.Decimals,
                direction, src.Buy, src.Sell, src.Symbol));
        }
        return list;
    }

    public static decimal Adjust(decimal source, MarginSide side) => side.Mode switch
    {
        MarginMode.Add => source + side.Value,
        MarginMode.Percent => source * (1m + side.Value / 100m),
        MarginMode.Multiply => source * side.Value,
        _ => source,
    };

    private static decimal Round(decimal value, int decimals) =>
        Math.Round(value, Math.Clamp(decimals, 0, 6), MidpointRounding.AwayFromZero);

    /// <summary>Kaydetmeden önce kural listesini doğrular; hata metinlerini döner.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<ProductRule> rules)
    {
        var errors = new List<string>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            var label = string.IsNullOrWhiteSpace(r.Name) ? r.Code : r.Name;
            if (string.IsNullOrWhiteSpace(r.Code)) errors.Add("Ürün kodu boş olamaz.");
            else if (!codes.Add(r.Code.Trim())) errors.Add($"{r.Code}: kod iki kez kullanılmış.");
            if (string.IsNullOrWhiteSpace(r.Name)) errors.Add($"{r.Code}: ürün adı boş olamaz.");
            if (string.IsNullOrWhiteSpace(r.SourceSymbol)) errors.Add($"{label}: kaynak sembol seçilmeli.");
            if (r.Decimals is < 0 or > 6) errors.Add($"{label}: ondalık 0-6 arası olmalı.");
            Check(r.Buy, "alış");
            Check(r.Sell, "satış");

            void Check(MarginSide? side, string name)
            {
                if (side is null) { errors.Add($"{label}: {name} marjı eksik."); return; }
                if (side.Mode == MarginMode.Multiply && side.Value <= 0)
                    errors.Add($"{label}: {name} katsayısı sıfırdan büyük olmalı.");
                if (side.Mode == MarginMode.Percent && side.Value <= -100)
                    errors.Add($"{label}: {name} yüzdesi -100 üzerinde olmalı.");
            }
        }
        return errors;
    }
}
