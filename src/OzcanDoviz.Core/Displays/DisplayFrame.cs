using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Core.Displays;

/// <summary>
/// LED/seri ekrana gidecek çerçeveyi üretir: [başlangıç baytları] başlık + satırlar [bitiş baytları].
/// Saf fonksiyon; tarayıcıdaki gönderici bu baytları COM veya Bluetooth'a olduğu gibi yazar.
/// </summary>
public static partial class DisplayFrame
{
    static DisplayFrame() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [GeneratedRegex(@"\{(\w+)(?:,(-?\d{1,3}))?\}")]
    private static partial Regex Field();

    public static IReadOnlyList<BoardItem> Select(DisplayScreen screen, IReadOnlyList<BoardItem> items)
    {
        var codes = screen.Layout.ProductCodes;
        if (codes.Count == 0) return items;
        var byCode = items.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        return codes.Select(c => byCode.GetValueOrDefault(c)).Where(i => i is not null).ToList()!;
    }

    /// <summary>Kodlamadan önceki metin (önizleme ve test için).</summary>
    public static string BuildText(DisplayScreen screen, IReadOnlyList<BoardItem> items, DateTime localNow)
    {
        var s = screen.Serial;
        // tr: 6.482,44 (binlik ayırıcılı) · invariant: 6482.44 (ayırıcısız; LED panellerde yer kazandırır)
        var tr = s.NumberCulture == "tr";
        var culture = tr ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.InvariantCulture;
        string Num(decimal v, int d) => v.ToString((tr ? "N" : "F") + d, culture);
        var separator = Unescape(s.LineSeparator);
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(s.HeaderTemplate))
            lines.Add(Fit(Render(s.HeaderTemplate, name => name switch
            {
                "time" => localNow.ToString("HH:mm", CultureInfo.InvariantCulture),
                "date" => localNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                "title" => screen.Layout.Title,
                _ => null,
            }), s));

        foreach (var i in Select(screen, items))
            lines.Add(Fit(Render(s.LineTemplate, name => name switch
            {
                "code" => i.Code,
                "name" => i.Name,
                "buy" => Num(i.Buy, i.Decimals),
                "sell" => Num(i.Sell, i.Decimals),
                "dir" => i.Direction == "up" ? "+" : i.Direction == "down" ? "-" : "=",
                _ => null,
            }), s));

        var text = string.Join(separator, lines) + (lines.Count > 0 ? separator : "");
        if (s.UpperCase) text = text.ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
        if (s.AsciiFold) text = Fold(text);
        return text;
    }

    public static byte[] Build(DisplayScreen screen, IReadOnlyList<BoardItem> items, DateTime localNow)
    {
        var s = screen.Serial;
        var encoding = s.Encoding switch
        {
            "utf-8" => new UTF8Encoding(false),
            "windows-1254" => Encoding.GetEncoding(1254),
            "iso-8859-9" => Encoding.GetEncoding("iso-8859-9"),
            _ => Encoding.ASCII,
        };
        var body = encoding.GetBytes(BuildText(screen, items, localNow));
        return [.. ParseHex(s.PrefixHex) ?? [], .. body, .. ParseHex(s.SuffixHex) ?? []];
    }

    /// <summary>"02", "1B 40", "0x02,0x03" → baytlar. Geçersizse null; boşsa boş dizi.</summary>
    public static byte[]? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return [];
        var parts = hex.Replace("0x", " ", StringComparison.OrdinalIgnoreCase)
            .Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<byte>();
        foreach (var p in parts)
        {
            if (p.Length % 2 != 0) return null;
            for (var i = 0; i < p.Length; i += 2)
            {
                if (!byte.TryParse(p.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return null;
                bytes.Add(b);
            }
        }
        return bytes.Count > 64 ? null : bytes.ToArray();
    }

    private static string Render(string template, Func<string, string?> value) =>
        Field().Replace(template, m =>
        {
            var v = value(m.Groups[1].Value);
            if (v is null) return m.Value; // bilinmeyen alan olduğu gibi kalır
            if (!m.Groups[2].Success) return v;
            var width = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var abs = Math.Abs(width);
            if (v.Length > abs) v = v[..abs];
            return width < 0 ? v.PadRight(abs) : v.PadLeft(abs);
        });

    private static string Fit(string line, SerialSettings s) =>
        s.MaxLineLength > 0 && line.Length > s.MaxLineLength ? line[..s.MaxLineLength] : line;

    private static string Unescape(string v) =>
        v.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");

    /// <summary>Türkçe harfleri ve aksanları ASCII'ye indirger; ASCII dışında kalanları siler.</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                'ç' => "c", 'Ç' => "C", 'ğ' => "g", 'Ğ' => "G", 'ı' => "i", 'İ' => "I",
                'ö' => "o", 'Ö' => "O", 'ş' => "s", 'Ş' => "S", 'ü' => "u", 'Ü' => "U",
                '₺' => "TL", '€' => "EUR", '$' => "$",
                _ when ch < 128 => ch.ToString(),
                _ => "",
            });
        }
        return sb.ToString();
    }
}
