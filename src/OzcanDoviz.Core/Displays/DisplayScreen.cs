namespace OzcanDoviz.Core.Displays;

public enum DisplayKind { Tv, Pano, Led }

/// <summary>
/// Ekrana nasıl ulaşılır. <see cref="Hdmi"/>: ekrana bağlı bilgisayarda tam ekran sayfa.
/// <see cref="Serial"/>: COM/RS-232/RS-485 (USB-seri ve Bluetooth SPP sanal COM dahil).
/// <see cref="Bluetooth"/>: BLE LED denetleyicisi (GATT karakteristiğine yazılır).
/// </summary>
public enum DisplayOutput { Hdmi, Serial, Bluetooth }

/// <summary>TV / pano / LED ekran tanımı. <c>App_Data/displays.json</c> dosyasında durur.</summary>
public sealed class DisplayScreen
{
    /// <summary>Adreste kullanılır: <c>/ekran/{id}</c>. Küçük harf, rakam ve tire.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DisplayKind Kind { get; set; } = DisplayKind.Tv;
    public DisplayOutput Output { get; set; } = DisplayOutput.Hdmi;
    public bool Enabled { get; set; } = true;
    public DisplayLayout Layout { get; set; } = new();
    public SerialSettings Serial { get; set; } = new();
    public BluetoothSettings Bluetooth { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>HDMI ekran görünümü (TV ve pano).</summary>
public sealed class DisplayLayout
{
    public string Title { get; set; } = "";
    /// <summary>dark, light, gold</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>landscape, portrait</summary>
    public string Orientation { get; set; } = "landscape";
    public int Columns { get; set; } = 2;
    public decimal FontScale { get; set; } = 1m;
    public bool ShowLogo { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool ShowTicker { get; set; } = true;
    public bool ShowBuy { get; set; } = true;
    public bool ShowSell { get; set; } = true;
    /// <summary>Gösterilecek ürün kodları, bu sırayla. Boşsa tüm yayındaki ürünler.</summary>
    public List<string> ProductCodes { get; set; } = [];
    /// <summary>Sayfa başına satır; 0 ise hepsi tek sayfada. Fazlası <see cref="PageSeconds"/> aralıkla döner.</summary>
    public int PageSize { get; set; }
    public int PageSeconds { get; set; } = 10;
}

/// <summary>
/// LED panele giden metin çerçevesi. Satır şablonunda {code} {name} {buy} {sell} {dir} alanları,
/// genişlik için {name,-12} (sola yasla) veya {sell,10} (sağa yasla) yazılır. Başlıkta {time} {date} kullanılabilir.
/// </summary>
public sealed class SerialSettings
{
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    /// <summary>none, even, odd</summary>
    public string Parity { get; set; } = "none";
    public int StopBits { get; set; } = 1;
    /// <summary>ascii, utf-8, windows-1254, iso-8859-9</summary>
    public string Encoding { get; set; } = "ascii";
    /// <summary>Türkçe harfleri ASCII karşılığına çevir (ç→c, ğ→g…). Çoğu LED denetleyicide Türkçe karakter yoktur.</summary>
    public bool AsciiFold { get; set; } = true;
    public bool UpperCase { get; set; } = true;
    public string HeaderTemplate { get; set; } = "";
    public string LineTemplate { get; set; } = "{code,-8}{buy,10}{sell,10}";
    /// <summary>Satır ayırıcı; \r \n \t kaçışları yazılabilir.</summary>
    public string LineSeparator { get; set; } = "\\r\\n";
    /// <summary>Çerçeve başı/sonu baytları, onaltılık (ör. "02" STX, "03" ETX, "1B 40").</summary>
    public string PrefixHex { get; set; } = "";
    public string SuffixHex { get; set; } = "";
    /// <summary>tr (6.482,44) veya invariant (6482.44)</summary>
    public string NumberCulture { get; set; } = "invariant";
    /// <summary>Satır en fazla bu kadar karakter (0 = sınırsız).</summary>
    public int MaxLineLength { get; set; }
    /// <summary>Gönderim aralığı; fiyat değişince de hemen gönderilir.</summary>
    public int IntervalSeconds { get; set; } = 5;
}

/// <summary>BLE LED denetleyicisi: servis ve yazılabilir karakteristik UUID'leri, paket boyu.</summary>
public sealed class BluetoothSettings
{
    public string ServiceUuid { get; set; } = "0000ffe0-0000-1000-8000-00805f9b34fb";
    public string CharacteristicUuid { get; set; } = "0000ffe1-0000-1000-8000-00805f9b34fb";
    /// <summary>BLE tek yazımda genelde 20 bayt alır.</summary>
    public int ChunkSize { get; set; } = 20;
}

public interface IDisplayStore
{
    Task<IReadOnlyList<DisplayScreen>> ListAsync(CancellationToken cancellationToken);
    Task<DisplayScreen?> FindAsync(string id, CancellationToken cancellationToken);
    Task<DisplayScreen> SaveAsync(DisplayScreen screen, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}

public static class DisplayRules
{
    private static readonly string[] Themes = ["dark", "light", "gold"];
    private static readonly string[] Encodings = ["ascii", "utf-8", "windows-1254", "iso-8859-9"];
    private static readonly int[] Bauds = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    public static IReadOnlyList<string> Validate(DisplayScreen s)
    {
        var e = new List<string>();
        if (!IsValidId(s.Id)) e.Add("Ekran kimliği 3-40 karakter olmalı; küçük harf, rakam ve tire.");
        if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 80) e.Add("Ekran adı 1-80 karakter olmalı.");

        var l = s.Layout;
        if (!Themes.Contains(l.Theme)) e.Add("Tema dark, light veya gold olmalı.");
        if (l.Orientation is not ("landscape" or "portrait")) e.Add("Yön yatay veya dikey olmalı.");
        if (l.Columns is < 1 or > 4) e.Add("Sütun sayısı 1-4 arası olmalı.");
        if (l.FontScale is < 0.5m or > 3m) e.Add("Yazı ölçeği 0,5-3 arası olmalı.");
        if (l.PageSize is < 0 or > 60) e.Add("Sayfa başına satır 0-60 arası olmalı.");
        if (l.PageSeconds is < 3 or > 300) e.Add("Sayfa süresi 3-300 sn olmalı.");
        if (!l.ShowBuy && !l.ShowSell) e.Add("Alış veya satıştan en az biri gösterilmeli.");
        if (l.ProductCodes.Count > 100) e.Add("En fazla 100 ürün seçilebilir.");

        var r = s.Serial;
        if (!Bauds.Contains(r.BaudRate)) e.Add($"Baud hızı şunlardan biri olmalı: {string.Join(", ", Bauds)}.");
        if (r.DataBits is not (7 or 8)) e.Add("Veri biti 7 veya 8 olmalı.");
        if (r.StopBits is not (1 or 2)) e.Add("Durma biti 1 veya 2 olmalı.");
        if (r.Parity is not ("none" or "even" or "odd")) e.Add("Eşlik none, even veya odd olmalı.");
        if (!Encodings.Contains(r.Encoding)) e.Add($"Kodlama şunlardan biri olmalı: {string.Join(", ", Encodings)}.");
        if (r.NumberCulture is not ("tr" or "invariant")) e.Add("Sayı biçimi tr veya invariant olmalı.");
        if (string.IsNullOrWhiteSpace(r.LineTemplate) || r.LineTemplate.Length > 200) e.Add("Satır şablonu 1-200 karakter olmalı.");
        if (r.HeaderTemplate.Length > 200) e.Add("Başlık şablonu en fazla 200 karakter olabilir.");
        if (DisplayFrame.ParseHex(r.PrefixHex) is null) e.Add("Başlangıç baytları onaltılık olmalı (ör. 02 veya 1B 40).");
        if (DisplayFrame.ParseHex(r.SuffixHex) is null) e.Add("Bitiş baytları onaltılık olmalı (ör. 03).");
        if (r.MaxLineLength is < 0 or > 500) e.Add("Satır uzunluğu 0-500 olmalı.");
        if (r.IntervalSeconds is < 1 or > 3600) e.Add("Gönderim aralığı 1-3600 sn olmalı.");

        var b = s.Bluetooth;
        if (s.Output == DisplayOutput.Bluetooth && (!Guid.TryParse(b.ServiceUuid, out _) || !Guid.TryParse(b.CharacteristicUuid, out _)))
            e.Add("Bluetooth servis ve karakteristik UUID'leri geçerli olmalı.");
        if (b.ChunkSize is < 1 or > 512) e.Add("Bluetooth paket boyu 1-512 bayt olmalı.");
        return e;
    }

    public static bool IsValidId(string? id) =>
        id is { Length: >= 3 and <= 40 } && id.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
