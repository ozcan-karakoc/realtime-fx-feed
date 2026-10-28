using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Realtime;

/// <summary>
/// Mobil ve dış istemciler için düz WebSocket: <c>wss://alanadi/ws/prices?key=odz_…</c>.
/// SignalR istemcisi gerektirmez; her mesaj tek bir JSON nesnesidir. Bağlanınca son tablo, sonra her değişim gelir.
/// İstemci "ping" gönderirse "pong" döner.
/// </summary>
public sealed class MobileSocketHub(IPriceBoard board, ILogger<MobileSocketHub> logger) : IPriceBroadcaster
{
    public const string Path = "/ws/prices";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public IReadOnlyList<(int ClientId, string ClientName, DateTime ConnectedUtc)> Connections() =>
        _connections.Values.Select(c => (c.ClientId, c.ClientName, c.ConnectedUtc)).ToList();

    public async Task HandleAsync(HttpContext context, AppDbContext db)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket isteği bekleniyor.");
            return;
        }

        var client = await ApiClientResolver.FindAsync(db, context.Request, context.RequestAborted);
        if (client is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Geçersiz veya pasif istemci anahtarı.");
            return;
        }

        client.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(context.RequestAborted);

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(socket, client.Id, client.Name, PublicPrices.ParseCodes(client.AllowedCodes));
        var id = Guid.NewGuid();
        _connections[id] = connection;
        logger.LogInformation("Mobil soket bağlandı: {Client} ({Count} bağlantı)", client.Name, _connections.Count);

        try
        {
            var first = PublicPrices.From(board.Current, connection.AllowedCodes);
            connection.LastItems = JsonSerializer.Serialize(first.Items, Json) + first.Stale;
            await connection.SendAsync(Serialize(first), context.RequestAborted);
            await ReceiveLoopAsync(connection, context.RequestAborted);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // istemci koptu
        }
        finally
        {
            _connections.TryRemove(id, out _);
            logger.LogInformation("Mobil soket kapandı: {Client}", client.Name);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch (WebSocketException) { }
            }
        }
    }

    public async Task BroadcastAsync(BoardSnapshot snapshot, CancellationToken cancellationToken)
    {
        foreach (var (id, connection) in _connections)
        {
            try
            {
                // Filtreli istemci, yalnızca kendi ürünleri değişince mesaj alır.
                var message = PublicPrices.From(snapshot, connection.AllowedCodes);
                var items = JsonSerializer.Serialize(message.Items, Json) + message.Stale;
                if (items == connection.LastItems) continue;
                connection.LastItems = items;
                await connection.SendAsync(Serialize(message), cancellationToken);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                _connections.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Site içeriği değişince bağlı mobil istemcilere haber verir; istemci içeriği REST'ten yeniden çeker.</summary>
    public async Task NotifyContentChangedAsync(CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes("""{"type":"content-updated"}""");
        foreach (var (id, connection) in _connections)
        {
            try { await connection.SendAsync(payload, cancellationToken); }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { _connections.TryRemove(id, out _); }
        }
    }

    private static async Task ReceiveLoopAsync(Connection connection, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (connection.Socket.State == WebSocketState.Open)
        {
            var result = await connection.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) continue;
            var text = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
            if (text.Equals("ping", StringComparison.OrdinalIgnoreCase))
                await connection.SendAsync(Encoding.UTF8.GetBytes("""{"type":"pong"}"""), ct);
        }
    }

    private static byte[] Serialize(PublicPriceMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

    private sealed class Connection(WebSocket socket, int clientId, string clientName, IReadOnlySet<string> allowedCodes)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public WebSocket Socket { get; } = socket;
        public int ClientId { get; } = clientId;
        public string ClientName { get; } = clientName;
        public IReadOnlySet<string> AllowedCodes { get; } = allowedCodes;
        public DateTime ConnectedUtc { get; } = DateTime.UtcNow;
        public string? LastItems { get; set; }

        // WebSocket aynı anda tek gönderim kabul eder; yayın ve pong çakışmasın.
        public async Task SendAsync(byte[] payload, CancellationToken ct)
        {
            await _send.WaitAsync(ct);
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _send.Release();
            }
        }
    }
}
