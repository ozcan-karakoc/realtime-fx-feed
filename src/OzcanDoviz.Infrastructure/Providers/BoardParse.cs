using System.Globalization;
using System.Text.Json;
using OzcanDoviz.Core.Prices;

namespace OzcanDoviz.Infrastructure.Providers;

/// <summary>
/// Ortak pano gövdelerini alış/satışa çevirir. Ortak sembol (HASALTIN, CEYREK, USDTRY)
/// ham kodun yanına eklenir; ikisi de yayınlanır, katalogda hangisi yazılıysa o eşleşir.
/// </summary>
public static class BoardParse
{
    private static readonly Dictionary<string, string> HaremAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALTIN"] = "HASALTIN",
        ["KULCEALTIN"] = "GRAMALTIN",
        ["CEYREK_YENI"] = "CEYREK",
        ["YARIM_YENI"] = "YARIM",
        ["TEK_YENI"] = "TAM",
        ["ATA_YENI"] = "ATA",
        ["ONS"] = "XAUUSD",
        ["GUMUSTRY"] = "GUMUSTRY",
    };

    public static IReadOnlyList<PriceQuote> Harem(string raw, DateTime now)
    {
        var start = raw.IndexOf('[');
        if (start < 0) return [];
        try
        {
            using var doc = JsonDocument.Parse(raw[start..]);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2)
                return [];
            var payload = doc.RootElement[1];
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("data", out var data))
                return [];
            if (data.ValueKind != JsonValueKind.Object) return [];

            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in data.EnumerateObject())
            {
                var item = prop.Value;
                if (item.ValueKind != JsonValueKind.Object) continue;
                var code = Text(item, "code") ?? prop.Name;
                var buy = Num(item, "alis");
                var sell = Num(item, "satis");
                if (buy is null || sell is null) continue;
                Add(list, seen, code, buy.Value, sell.Value, now);
                if (HaremAlias.TryGetValue(code, out var alias))
                    Add(list, seen, alias, buy.Value, sell.Value, now);
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Socket.IO <c>42</c> önekini atıp kayda yazılacak JSON dizisini verir.</summary>
    public static string? HaremJson(string raw)
    {
        var start = raw.IndexOf('[');
        return start < 0 ? null : raw[start..];
    }

    public static IReadOnlyList<PriceQuote> Ahlatci(string raw, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var smb = Text(item, "SMB");
                var buy = Num(item, "Al");
                var sell = Num(item, "St");
                if (smb is null || buy is null || sell is null) continue;
                Add(list, seen, smb, buy.Value, sell.Value, now);
                var alias = AhlatciAlias(smb, buy.Value);
                if (alias is not null) Add(list, seen, alias, buy.Value, sell.Value, now);
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<PriceQuote> Ozbey(string raw, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var code = Text(prop.Value, "title") ?? prop.Name;
                var buy = Num(prop.Value, "alis");
                var sell = Num(prop.Value, "satis");
                if (buy is null || sell is null) continue;
                // Kaynak bazı çiftlerde alış ve satışı ters yazıyor. Ham JSON olduğu gibi saklanır;
                // panoda alış, iki sayıdan küçük olanıdır.
                var lo = buy.Value <= sell.Value ? buy.Value : sell.Value;
                var hi = buy.Value <= sell.Value ? sell.Value : buy.Value;
                Add(list, seen, code, lo, hi, now);
                if (code.Equals("GMSTRY", StringComparison.OrdinalIgnoreCase))
                    Add(list, seen, "GUMUSTRY", lo, hi, now);
                if (code.Equals("KGRTRY", StringComparison.OrdinalIgnoreCase) && lo > 10_000m)
                    Add(list, seen, "HASALTIN", lo / 1000m, hi / 1000m, now);
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<PriceQuote> Kitco(string raw, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];
            if (!data.TryGetProperty("GetMetalQuoteV3", out var quote)) return [];
            if (!quote.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return [];
            var metal = Text(quote, "symbol") ?? "AU";
            var symbol = metal.ToUpperInvariant() switch
            {
                "AU" or "XAU" => "XAUUSD",
                "AG" or "XAG" => "XAGUSD",
                "PT" => "XPTUSD",
                "PD" => "XPDUSD",
                _ => metal.ToUpperInvariant(),
            };
            foreach (var row in results.EnumerateArray())
            {
                var bid = Num(row, "bid");
                var ask = Num(row, "ask");
                if (bid is null || ask is null) continue;
                var lo = bid.Value <= ask.Value ? bid.Value : ask.Value;
                var hi = bid.Value <= ask.Value ? ask.Value : bid.Value;
                return [new PriceQuote(symbol, lo, hi, now)];
            }
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Nesne listesinde alış/satış arar. <c>["value1","value2"]</c> gibi düz metin dizisi boş döner.</summary>
    public static IReadOnlyList<PriceQuote> Nadir(string raw, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var rows = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("data", out var data) ? data : default;
            if (rows.ValueKind != JsonValueKind.Array) return [];
            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in rows.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var code = Text(item, "SMB") ?? Text(item, "code") ?? Text(item, "kod") ?? Text(item, "Kod") ?? Text(item, "symbol");
                var buy = Num(item, "Al") ?? Num(item, "alis") ?? Num(item, "Alis") ?? Num(item, "buy");
                var sell = Num(item, "St") ?? Num(item, "satis") ?? Num(item, "Satis") ?? Num(item, "sell");
                if (code is null || buy is null || sell is null) continue;
                Add(list, seen, code, buy.Value, sell.Value, now);
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Sağlamoğlu TL panosu. Alış <c>customerBuysAt</c>, satış <c>customerSellsAt</c>
    /// (sitenin alış/satış sütunları). Bilinen kimlikler katalog sembolüne yazılır;
    /// diğer satırlar kendi kodu ve adıyla gelir, katalogda yoksa Kur/TV listesine eklenir.
    /// </summary>
    public static IReadOnlyList<PriceQuote> Saglamoglu(string productsJson, string pricesJson, DateTime now)
    {
        try
        {
            var names = ProductNames(productsJson);
            using var prices = JsonDocument.Parse(pricesJson);
            if (!prices.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                if (row.TryGetProperty("isVisible", out var visible) && visible.ValueKind == JsonValueKind.False) continue;
                if (!row.TryGetProperty("marketProductId", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                var buy = Num(row, "customerBuysAt");
                var sell = Num(row, "customerSellsAt");
                if (buy is null || sell is null || buy <= 0 || sell <= 0) continue;
                if (!names.TryGetValue(id, out var product)) continue;

                var symbol = SymbolOf(id, product.Name, seen);
                list.Add(new PriceQuote(symbol, buy.Value, sell.Value, now, product.Name));
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Sağlamoğlu ürün kimliği → katalog sembolü. Harem’deki yeni ziynet, katalog koduna gider.</summary>
    private static readonly Dictionary<int, string> SaglamogluCatalog = new()
    {
        [1] = "HASALTIN",
        [86] = "GRAMALTIN",
        [35] = "CEYREK",
        [36] = "CEYREK_ESKI",
        [37] = "YARIM",
        [38] = "YARIM_ESKI",
        [39] = "TAM",
        [40] = "TAM_ESKI",
        [41] = "GREMSE_YENI",
        [42] = "GREMSE",
        [49] = "ATA_YENI",
        [50] = "ATA",
        [9] = "USDTRY",
        [10] = "EURTRY",
        [11] = "GBPTRY",
        [12] = "CHFTRY",
        [13] = "JPYTRY",
        [14] = "CADTRY",
        [15] = "AUDTRY",
        [16] = "SARTRY",
        [2] = "XAUUSD",
        [6] = "GUMUSTRY",
    };

    private readonly record struct SaglamogluProduct(string Name, string Section);

    private static Dictionary<int, SaglamogluProduct> ProductNames(string productsJson)
    {
        var map = new Dictionary<int, SaglamogluProduct>();
        using var doc = JsonDocument.Parse(productsJson);
        if (!doc.RootElement.TryGetProperty("data", out var sections) || sections.ValueKind != JsonValueKind.Array)
            return map;
        foreach (var section in sections.EnumerateArray())
        {
            var key = Text(section, "key") ?? "";
            if (!section.TryGetProperty("productList", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                var name = Text(item, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                map[id] = new SaglamogluProduct(name.Trim(), key);
            }
        }
        return map;
    }

    private static string SymbolOf(int id, string name, HashSet<string> seen)
    {
        var symbol = SaglamogluCatalog.TryGetValue(id, out var catalog) ? catalog : Slug(name, id);
        if (seen.Add(symbol)) return symbol;
        var idText = id.ToString(CultureInfo.InvariantCulture);
        var withId = symbol.Length + idText.Length <= 20 ? symbol + idText : symbol[..Math.Max(1, 20 - idText.Length)] + idText;
        seen.Add(withId);
        return withId;
    }

    private static string Slug(string name, int id)
    {
        var upper = name.ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
        var chars = new List<char>(upper.Length);
        foreach (var ch in upper)
        {
            var mapped = ch switch
            {
                'Ç' => 'C',
                'Ğ' => 'G',
                'İ' => 'I',
                'Ö' => 'O',
                'Ş' => 'S',
                'Ü' => 'U',
                _ => ch,
            };
            if (mapped is >= 'A' and <= 'Z' or >= '0' and <= '9') chars.Add(mapped);
        }
        var slug = chars.Count == 0 ? "U" + id.ToString(CultureInfo.InvariantCulture) : new string(chars.ToArray());
        if (slug.Length <= 20) return slug;
        var idText = id.ToString(CultureInfo.InvariantCulture);
        return slug[..Math.Max(1, 20 - idText.Length)] + idText;
    }

    public static int RowCount(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array) return doc.RootElement.GetArrayLength();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                return data.GetArrayLength();
            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string? AhlatciAlias(string smb, decimal buy)
    {
        if (smb.Equals("XAU", StringComparison.OrdinalIgnoreCase))
            return buy is >= 1500m and < 6000m ? "XAUUSD" : "HASALTIN";
        if (smb.Equals("XAG", StringComparison.OrdinalIgnoreCase))
            return "GUMUSTRY";
        if (smb.Length == 3 && smb.All(char.IsLetter))
            return smb.ToUpperInvariant() + "TRY";
        return null;
    }

    private static void Add(List<PriceQuote> list, HashSet<string> seen, string symbol, decimal buy, decimal sell, DateTime now)
    {
        if (buy <= 0 || sell <= 0) return;
        if (!seen.Add(symbol)) return;
        list.Add(new PriceQuote(symbol, buy, sell, now));
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static decimal? Num(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => ParseDecimal(el.GetString()),
            _ => null,
        };
    }

    internal static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.Contains(',') && s.Contains('.'))
            s = s.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        else if (s.Contains(','))
            s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
