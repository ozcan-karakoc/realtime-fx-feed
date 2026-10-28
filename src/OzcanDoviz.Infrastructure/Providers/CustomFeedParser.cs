using System.Text.Json;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Providers;

namespace OzcanDoviz.Infrastructure.Providers;

/// <summary>Panelde tanımlanan eşlemeyle bir mesajı/gövdeyi fiyatlara çevirir. Saf fonksiyon.</summary>
public static class CustomFeedParser
{
    /// <returns>Fiyat yoksa veya mesaj veri taşımıyorsa boş liste.</returns>
    public static IReadOnlyList<PriceQuote> Parse(string message, CustomProviderDefinition d, DateTime now)
    {
        var json = message;
        if (d.UsesSocketIo)
        {
            // Engine.IO 4: veri paketi "42[...]" (ad alanı varsa "42/ns,[...]").
            if (!message.StartsWith("42", StringComparison.Ordinal)) return [];
            var start = message.IndexOf('[');
            if (start < 0) return [];
            json = message[start..];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryNavigate(doc.RootElement, d.DataPath, out var data)) return [];

            var aliases = d.AliasMap();
            var list = new List<PriceQuote>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    Add(item, null);
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                // Tek kayıt mı (alış alanı doğrudan burada), sembol→nesne sözlüğü mü?
                if (TryNavigate(data, d.BuyField, out _)) Add(data, null);
                else
                    foreach (var prop in data.EnumerateObject())
                        Add(prop.Value, prop.Name);
            }
            return list;

            void Add(JsonElement item, string? key)
            {
                if (item.ValueKind != JsonValueKind.Object) return;
                var symbol = Text(item, d.SymbolField) ?? key;
                var buy = Number(item, d.BuyField);
                var sell = Number(item, d.SellField);
                if (string.IsNullOrWhiteSpace(symbol) || buy is not > 0 || sell is not > 0) return;
                symbol = symbol.Trim().ToUpperInvariant();
                var name = string.IsNullOrWhiteSpace(d.NameField) ? null : Text(item, d.NameField);
                if (seen.Add(symbol)) list.Add(new PriceQuote(symbol, buy.Value, sell.Value, now, name));
                if (aliases.TryGetValue(symbol, out var alias) && seen.Add(alias))
                    list.Add(new PriceQuote(alias, buy.Value, sell.Value, now, name));
            }
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>"data", "1.data", "result.items.0" gibi yollarla gezer. Boş yol = kök.</summary>
    public static bool TryNavigate(JsonElement root, string? path, out JsonElement result)
    {
        result = root;
        if (string.IsNullOrWhiteSpace(path)) return true;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (result.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index))
            {
                if (index < 0 || index >= result.GetArrayLength()) return false;
                result = result[index];
            }
            else if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty(part, out var child))
                result = child;
            else
                return false;
        }
        return true;
    }

    private static string? Text(JsonElement obj, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !TryNavigate(obj, path, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null,
        };
    }

    private static decimal? Number(JsonElement obj, string path)
    {
        if (!TryNavigate(obj, path, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var v) ? v : null,
            JsonValueKind.String => BoardParse.ParseDecimal(el.GetString()),
            _ => null,
        };
    }
}
