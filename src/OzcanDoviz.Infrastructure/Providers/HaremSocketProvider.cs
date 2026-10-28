using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Providers;

namespace OzcanDoviz.Infrastructure.Providers;

/// <summary>
/// Harem Altın socket.io (Engine.IO 4, websocket). Bağlantı süreç boyunca tektir; fiyat isteği yeni soket
/// açmaz, bellekteki son paketi döner. Kopunca 1 sn'den 15 sn'ye artan beklemeyle yeniden bağlanır.
/// </summary>
public sealed class HaremSocketProvider(ILogger<HaremSocketProvider> logger, IHostApplicationLifetime lifetime)
    : IPriceProvider
{
    public const string Url = "wss://socket.haremaltin.com:443/socket.io/?EIO=4&transport=websocket";

    private readonly object _gate = new();
    private IReadOnlyList<PriceQuote> _quotes = [];
    private string? _error;
    private int _started;

    public string Key => "harem";
    public string DisplayName => "Harem Altın";
    public string Description => "Canlı socket.io akışı. Altın, ziynet, döviz ve parite.";
    public ProviderTransport Transport => ProviderTransport.Socket;

    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        if (Snapshot().Count == 0)
            await WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        var quotes = Snapshot();
        if (quotes.Count == 0)
            throw new InvalidOperationException(ErrorText() ?? "Harem henüz fiyat göndermedi.");
        return quotes;
    }

    private IReadOnlyList<PriceQuote> Snapshot() { lock (_gate) return _quotes; }
    private string? ErrorText() { lock (_gate) return _error; }

    private void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(() => LoopAsync(lifetime.ApplicationStopping));
    }

    private async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Snapshot().Count == 0 && Environment.TickCount64 < until)
            await Task.Delay(150, ct);
    }

    private async Task LoopAsync(CancellationToken stopping)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(stopping);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lock (_gate) _error = "Harem soketi koptu: " + ex.Message;
                logger.LogWarning(ex, "Harem soketi koptu");
            }

            try { await Task.Delay(delay, stopping); }
            catch (OperationCanceledException) { return; }
            if (delay < TimeSpan.FromSeconds(15)) delay += TimeSpan.FromSeconds(2);
        }
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://www.haremaltin.com");
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await ws.ConnectAsync(new Uri(Url), ct);
        logger.LogInformation("Harem soketi açıldı");

        var buf = new byte[64 * 1024];
        using var msg = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            msg.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buf, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                msg.Write(buf, 0, result.Count);
                if (msg.Length > 512 * 1024) throw new InvalidOperationException("Harem mesajı beklenenden büyük.");
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text || msg.Length == 0) continue;
            var text = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);

            if (text[0] == '0')
            {
                // Engine.IO açılışı: socket.io ad alanına bağlan.
                await SendAsync(ws, "40", ct);
                continue;
            }
            if (text.StartsWith("42", StringComparison.Ordinal))
            {
                var quotes = BoardParse.Harem(text, DateTime.UtcNow);
                if (quotes.Count == 0) continue;
                lock (_gate)
                {
                    // price_changed bazen yalnızca değişen sembolleri taşır; öncekiler korunur.
                    var merged = new Dictionary<string, PriceQuote>(StringComparer.OrdinalIgnoreCase);
                    foreach (var old in _quotes) merged[old.Symbol] = old;
                    foreach (var q in quotes) merged[q.Symbol] = q;
                    _quotes = merged.Values.ToList();
                    _error = null;
                }
                continue;
            }
            if (text[0] == '2')
                await SendAsync(ws, "3", ct); // ping → pong
        }
    }

    private static Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}
