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

/// <summary>Son marjlı fiyat tablosu. Web, REST ve WebSocket aynı nesneden okur.</summary>
public interface IPriceBoard
{
    BoardSnapshot Current { get; }
    /// <summary>Sağlayıcının son ham sembolleri (marj ekranındaki kaynak listesi için).</summary>
    IReadOnlyList<PriceQuote> RawQuotes { get; }
    void Publish(BoardSnapshot snapshot, IReadOnlyList<PriceQuote> raw);
}

/// <summary>Yeni tablo yayınlandığında çağrılır (SignalR, mobil WebSocket).</summary>
public interface IPriceBroadcaster
{
    Task BroadcastAsync(BoardSnapshot snapshot, CancellationToken cancellationToken);
}
