using OzcanDoviz.Core.Prices;

namespace OzcanDoviz.Core.Pricing;

public sealed record BoardSnapshot(
    string Provider,
    string ProviderName,
    DateTime UpdatedUtc,
    IReadOnlyList<BoardItem> Items,
    string? Error)
{
    public static readonly BoardSnapshot Empty = new("", "", DateTime.MinValue, [], "Henüz fiyat alınmadı.");
}

/// <summary>Son marjlı fiyat tablosu. Web, REST, WebSocket ve kur sabitleme aynı nesneden okur.</summary>
public interface IPriceBoard
{
    BoardSnapshot Current { get; }
    /// <summary>Genel sağlayıcının son ham sembolleri.</summary>
    IReadOnlyList<PriceQuote> RawQuotes { get; }
    /// <summary>Okunan her sağlayıcının son ham sembolleri (marj ekranındaki kaynak listesi için).</summary>
    IReadOnlyDictionary<string, IReadOnlyList<PriceQuote>> RawByProvider { get; }
    void Publish(BoardSnapshot snapshot, IReadOnlyDictionary<string, IReadOnlyList<PriceQuote>> rawByProvider);
}

/// <summary>Yeni tablo yayınlandığında çağrılır (SignalR, mobil WebSocket).</summary>
public interface IPriceBroadcaster
{
    Task BroadcastAsync(BoardSnapshot snapshot, CancellationToken cancellationToken);
}
