namespace OzcanDoviz.Core.Rates;

public sealed record HistoryCode(string Code, string Name, string Group, int Decimals);

/// <summary>Bir günün kapanışı. <see cref="Date"/> Harem'in kayit_tarihi'nin günüdür.</summary>
public sealed record HistoryPoint(DateOnly Date, decimal Buy, decimal Sell);

public sealed record HistorySeries(string Code, string Name, int Decimals, IReadOnlyList<HistoryPoint> Points, int MissingDays);

/// <summary>Grafik için günlük kapanış serisi (Harem geçmiş kurları, önbellekli).</summary>
public interface IRateHistoryService
{
    IReadOnlyList<HistoryCode> Codes { get; }
    Task<HistorySeries?> GetSeriesAsync(string code, int days, CancellationToken cancellationToken);
}

public static class HistoryCodes
{
    /// <summary>Sitede seçilebilen semboller. Grup: Harem'in geçmiş kur tipi (altın tip=1, döviz tip=0).</summary>
    public static readonly IReadOnlyList<HistoryCode> All =
    [
        new("ALTIN", "Has Altın (gr)", "altin", 2),
        new("KULCEALTIN", "Gram Altın", "altin", 2),
        new("AYAR22", "22 Ayar (gr)", "altin", 2),
        new("CEYREK_YENI", "Yeni Çeyrek", "altin", 0),
        new("YARIM_YENI", "Yeni Yarım", "altin", 0),
        new("TEK_YENI", "Yeni Tam", "altin", 0),
        new("ATA_YENI", "Yeni Ata", "altin", 0),
        new("ONS", "Ons Altın ($)", "altin", 2),
        new("GUMUSTRY", "Gümüş (gr)", "altin", 2),
        new("USDTRY", "Amerikan Doları", "doviz", 4),
        new("EURTRY", "Euro", "doviz", 4),
        new("GBPTRY", "İngiliz Sterlini", "doviz", 4),
        new("CHFTRY", "İsviçre Frangı", "doviz", 4),
        new("SARTRY", "Suudi Riyali", "doviz", 4),
        new("EURUSD", "Euro / Dolar", "doviz", 4),
    ];

    public static readonly int[] AllowedDays = [7, 30, 90];

    public static HaremHistoryType TypeOf(HistoryCode c) => c.Group == "doviz" ? HaremHistoryType.Currency : HaremHistoryType.Gold;
}
