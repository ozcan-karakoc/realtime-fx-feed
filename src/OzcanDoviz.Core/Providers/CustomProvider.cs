namespace OzcanDoviz.Core.Providers;

/// <summary>
/// Panelden eklenen fiyat sağlayıcı. Adres ws://, wss:// (canlı akış) veya http(s):// (yoklama) olabilir.
/// Gelen JSON <see cref="DataPath"/> ile gezilir; liste ya da sembol→nesne sözlüğü olabilir.
/// <c>App_Data/providers.json</c> dosyasında durur.
/// </summary>
public sealed class CustomProviderDefinition
{
    /// <summary>Benzersiz anahtar (küçük harf, rakam, tire). Hazır sağlayıcı anahtarlarıyla çakışamaz.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>auto, raw, socketio. auto: adres /socket.io/ içeriyorsa socket.io (Engine.IO 4), değilse düz JSON.</summary>
    public string Protocol { get; set; } = "auto";
    /// <summary>Bağlanınca gönderilecek abonelik mesajı (ör. {"action":"subscribe"}). socket.io'da 42["olay",…] biçiminde yazılır.</summary>
    public string? SubscribeMessage { get; set; }
    /// <summary>Bazı akışlar Origin başlığı ister (ör. https://www.ornek.com).</summary>
    public string? Origin { get; set; }
    /// <summary>
    /// Fiyat listesinin yolu, noktayla ayrılmış; dizi sırası sayıyla. Boş = kök.
    /// Örnekler: "data", "result.items", socket.io için "1.data" (42["olay", {data}]).
    /// </summary>
    public string DataPath { get; set; } = "";
    /// <summary>Sembol alanı. Veri sembol→nesne sözlüğüyse ve alan yoksa anahtar kullanılır.</summary>
    public string SymbolField { get; set; } = "code";
    public string BuyField { get; set; } = "alis";
    public string SellField { get; set; } = "satis";
    public string? NameField { get; set; }
    /// <summary>Her satır "HAM=ORTAK" (ör. ALTIN=HASALTIN). Ham kodun yanına ortak kod da yayınlanır.</summary>
    public string? Aliases { get; set; }

    public bool IsSocket => Url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || Url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    public bool UsesSocketIo => Protocol == "socketio"
        || (Protocol == "auto" && IsSocket && Url.Contains("/socket.io/", StringComparison.OrdinalIgnoreCase));

    public Dictionary<string, string> AliasMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (Aliases ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim().ToUpperInvariant();
        }
        return map;
    }
}

public sealed record CustomProviderTestResult(bool Ok, string Message, string? Sample, IReadOnlyList<string> Symbols);

public interface ICustomProviderService
{
    IReadOnlyList<CustomProviderDefinition> List();
    Task<CustomProviderDefinition> SaveAsync(CustomProviderDefinition definition, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);
    /// <summary>Kaydetmeden dener: bağlanır, ilk veriyi bekler, eşlemeyi uygular.</summary>
    Task<CustomProviderTestResult> TestAsync(CustomProviderDefinition definition, CancellationToken cancellationToken);
}

public static class CustomProviderRules
{
    public static IReadOnlyList<string> Validate(CustomProviderDefinition d, IEnumerable<string> builtInKeys)
    {
        var e = new List<string>();
        if (d.Key is not { Length: >= 3 and <= 30 } || !d.Key.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            e.Add("Anahtar 3-30 karakter olmalı; küçük harf, rakam ve tire.");
        else if (builtInKeys.Contains(d.Key, StringComparer.OrdinalIgnoreCase))
            e.Add($"'{d.Key}' hazır bir sağlayıcının anahtarı; başka bir anahtar seçin.");
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 60) e.Add("Ad 1-60 karakter olmalı.");

        if (!Uri.TryCreate(d.Url, UriKind.Absolute, out var u) || u.Scheme is not ("ws" or "wss" or "http" or "https"))
            e.Add("Adres ws://, wss://, http:// veya https:// ile başlamalı.");
        else if (!string.IsNullOrEmpty(u.UserInfo))
            e.Add("Adreste kullanıcı adı/şifre olamaz.");
        if (d.Url.Length > 500) e.Add("Adres en fazla 500 karakter olabilir.");

        if (d.Protocol is not ("auto" or "raw" or "socketio")) e.Add("Protokol auto, raw veya socketio olmalı.");
        if (d.Protocol == "socketio" && !d.IsSocket) e.Add("socket.io yalnızca ws:// veya wss:// adresiyle kullanılır.");
        if ((d.SubscribeMessage ?? "").Length > 2000) e.Add("Abonelik mesajı en fazla 2000 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(d.Origin) && !Uri.TryCreate(d.Origin, UriKind.Absolute, out _)) e.Add("Origin geçerli bir adres olmalı.");
        if ((d.DataPath ?? "").Length > 100) e.Add("Veri yolu en fazla 100 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(d.BuyField) || string.IsNullOrWhiteSpace(d.SellField)) e.Add("Alış ve satış alanları boş olamaz.");
        if ((d.Aliases ?? "").Length > 4000) e.Add("Eşleme listesi çok uzun.");
        return e;
    }
}
