using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Infrastructure.Providers;

/// <summary>ws:// / wss:// bağlantısı: düz JSON veya socket.io (Engine.IO 4) el sıkışması, ping/pong, abonelik mesajı.</summary>
internal static class SocketFeed
{
    private const int MaxMessage = 2 * 1024 * 1024;

    public static async Task RunAsync(CustomProviderDefinition d, Func<string, Task> onText, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(d.Origin)) ws.Options.SetRequestHeader("Origin", d.Origin.Trim());
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await ws.ConnectAsync(new Uri(d.Url), ct);

        var socketIo = d.UsesSocketIo;
        if (!socketIo && !string.IsNullOrWhiteSpace(d.SubscribeMessage))
            await SendAsync(ws, d.SubscribeMessage, ct);

        var buffer = new byte[64 * 1024];
        using var msg = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            msg.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                msg.Write(buffer, 0, result.Count);
                if (msg.Length > MaxMessage) throw new InvalidOperationException("Mesaj 2 MB sınırını aştı.");
            }
            while (!result.EndOfMessage);
            if (result.MessageType != WebSocketMessageType.Text || msg.Length == 0) continue;

            var text = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);
            if (socketIo)
            {
                if (text[0] == '0') { await SendAsync(ws, "40", ct); continue; }        // açılış → ad alanına bağlan
                if (text.StartsWith("40", StringComparison.Ordinal))                     // bağlandı → abone ol
                {
                    if (!string.IsNullOrWhiteSpace(d.SubscribeMessage)) await SendAsync(ws, d.SubscribeMessage, ct);
                    continue;
                }
                if (text == "2") { await SendAsync(ws, "3", ct); continue; }             // ping → pong
            }
            await onText(text);
        }
    }

    private static Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}

/// <summary>Panelden eklenen ws/wss sağlayıcı. Bağlantı süreç boyunca tektir; kopunca artan beklemeyle yeniden bağlanır.</summary>
internal sealed class CustomSocketProvider(CustomProviderDefinition d, ILogger logger, CancellationToken appStopping)
    : IPriceProvider, IDisposable
{
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
    private readonly object _gate = new();
    private Dictionary<string, PriceQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private string? _error;
    private int _started;

    public CustomProviderDefinition Definition => d;
    public string Key => d.Key;
    public string DisplayName => d.Name;
    public string Description => $"Özel · {(d.UsesSocketIo ? "socket.io" : "WebSocket")} · {new Uri(d.Url).Host}";
    public ProviderTransport Transport => ProviderTransport.Socket;

    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) _ = Task.Run(LoopAsync);
        var until = Environment.TickCount64 + 8000;
        while (Count() == 0 && Environment.TickCount64 < until) await Task.Delay(150, cancellationToken);
        lock (_gate)
        {
            if (_quotes.Count == 0) throw new InvalidOperationException(_error ?? $"{d.Name} henüz fiyat göndermedi.");
            return _quotes.Values.ToList();
        }
    }

    private int Count() { lock (_gate) return _quotes.Count; }

    private async Task LoopAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await SocketFeed.RunAsync(d, text =>
                {
                    var quotes = CustomFeedParser.Parse(text, d, DateTime.UtcNow);
                    if (quotes.Count == 0) return Task.CompletedTask;
                    lock (_gate)
                    {
                        // Kısmi güncellemede önceki semboller korunur.
                        var merged = new Dictionary<string, PriceQuote>(_quotes, StringComparer.OrdinalIgnoreCase);
                        foreach (var q in quotes) merged[q.Symbol] = q;
                        _quotes = merged;
                        _error = null;
                    }
                    delay = TimeSpan.FromSeconds(1);
                    return Task.CompletedTask;
                }, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lock (_gate) _error = $"{d.Name} bağlantısı koptu: {ex.Message}";
                logger.LogWarning("Özel sağlayıcı {Key} koptu: {Error}", d.Key, ex.Message);
            }
            try { await Task.Delay(delay, _cts.Token); }
            catch (OperationCanceledException) { return; }
            if (delay < TimeSpan.FromSeconds(30)) delay += TimeSpan.FromSeconds(2);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>Panelden eklenen http(s) sağlayıcı: her okumada GET, aynı eşleme.</summary>
internal sealed class CustomHttpProvider(CustomProviderDefinition d, IHttpClientFactory http) : IPriceProvider
{
    public CustomProviderDefinition Definition => d;
    public string Key => d.Key;
    public string DisplayName => d.Name;
    public string Description => $"Özel · HTTP · {new Uri(d.Url).Host}";
    public ProviderTransport Transport => ProviderTransport.Http;

    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken)
    {
        var body = await CustomProviderService.ReadHttpAsync(http, d, cancellationToken);
        var quotes = CustomFeedParser.Parse(body, d, DateTime.UtcNow);
        if (quotes.Count == 0) throw new InvalidOperationException($"{d.Name} cevap verdi ama eşlemeyle fiyat okunamadı.");
        return quotes;
    }
}

public sealed class CustomProviderDocument
{
    public List<CustomProviderDefinition> Providers { get; set; } = [];
}

/// <summary>
/// Özel sağlayıcıların tanımları (<c>App_Data/providers.json</c>) ve çalışan örnekleri.
/// Kaydedince eski bağlantı kapanır, yenisi açılır; yeniden başlatma gerekmez.
/// </summary>
public sealed class CustomProviderService : ICustomProviderService, IDisposable
{
    private readonly JsonFileStore<CustomProviderDocument> _file;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<CustomProviderService> _logger;
    private readonly CancellationToken _stopping;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyList<IPriceProvider> _providers = [];
    private List<CustomProviderDefinition> _definitions = [];

    public CustomProviderService(IOptions<StorageOptions> options, IHostEnvironment env, IHttpClientFactory http,
        IHostApplicationLifetime lifetime, ILogger<CustomProviderService> logger)
    {
        _http = http;
        _logger = logger;
        _stopping = lifetime.ApplicationStopping;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<CustomProviderDocument>(dir, "providers.json", options.Value.KeepHistory, () => new CustomProviderDocument(), logger);
        // Başlangıçta bir kez, eşzamanlı okunur (küçük dosya).
        Rebuild(_file.ReadAsync(CancellationToken.None).GetAwaiter().GetResult().Providers);
    }

    /// <summary>Çalışan (açık) özel sağlayıcılar.</summary>
    public IReadOnlyList<IPriceProvider> Providers => _providers;

    public IReadOnlyList<CustomProviderDefinition> List() => _definitions;

    public async Task<CustomProviderDefinition> SaveAsync(CustomProviderDefinition definition, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            definition.Key = definition.Key.Trim().ToLowerInvariant();
            definition.Url = definition.Url.Trim();
            var list = _definitions.Where(x => x.Key != definition.Key).Append(definition).OrderBy(x => x.Name).ToList();
            await _file.WriteAsync(new CustomProviderDocument { Providers = list }, cancellationToken);
            Rebuild(list);
            return definition;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = _definitions.Where(x => !x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == _definitions.Count) return false;
            await _file.WriteAsync(new CustomProviderDocument { Providers = list }, cancellationToken);
            Rebuild(list);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CustomProviderTestResult> TestAsync(CustomProviderDefinition d, CancellationToken cancellationToken)
    {
        string? sample = null;
        IReadOnlyList<PriceQuote> quotes = [];
        try
        {
            if (d.IsSocket)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await SocketFeed.RunAsync(d, text =>
                    {
                        sample ??= text;
                        var parsed = CustomFeedParser.Parse(text, d, DateTime.UtcNow);
                        if (parsed.Count > 0)
                        {
                            sample = text;
                            quotes = parsed;
                            timeout.Cancel(); // ilk fiyatlı mesaj yeterli
                        }
                        return Task.CompletedTask;
                    }, timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }
            else
            {
                sample = await ReadHttpAsync(_http, d, cancellationToken);
                quotes = CustomFeedParser.Parse(sample, d, DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or InvalidOperationException or UriFormatException
                                   || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new CustomProviderTestResult(false, $"Bağlanılamadı: {ex.Message}", Trim(sample), []);
        }

        var symbols = quotes.Select(q => q.Symbol).Take(60).ToList();
        if (quotes.Count > 0)
            return new CustomProviderTestResult(true, $"Bağlantı kuruldu, {quotes.Count} sembol okundu.", Trim(sample), symbols);
        return sample is null
            ? new CustomProviderTestResult(false, "Bağlantı kuruldu ama 10 sn içinde mesaj gelmedi. Abonelik mesajı gerekiyor olabilir.", null, [])
            : new CustomProviderTestResult(false, "Mesaj geldi ama eşlemeyle fiyat okunamadı. Veri yolu ve alan adlarını örnek mesaja göre düzeltin.", Trim(sample), []);
    }

    internal static async Task<string> ReadHttpAsync(IHttpClientFactory http, CustomProviderDefinition d, CancellationToken ct)
    {
        var client = http.CreateClient(HttpProvider.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, d.Url);
        if (!string.IsNullOrWhiteSpace(d.Origin)) request.Headers.TryAddWithoutValidation("Origin", d.Origin.Trim());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{d.Name} {(int)response.StatusCode} döndü.");
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidOperationException("Cevap 2 MB sınırını aştı.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string? Trim(string? s) => s is null ? null : s.Length > 1500 ? s[..1500] + "…" : s;

    private void Rebuild(List<CustomProviderDefinition> definitions)
    {
        foreach (var old in _providers.OfType<IDisposable>()) old.Dispose();
        _definitions = definitions;
        _providers = definitions.Where(x => x.Enabled)
            .Select(x => x.IsSocket
                ? (IPriceProvider)new CustomSocketProvider(x, _logger, _stopping)
                : new CustomHttpProvider(x, _http))
            .ToList();
        _logger.LogInformation("Özel sağlayıcılar yüklendi: {Count}", _providers.Count);
    }

    public void Dispose()
    {
        foreach (var p in _providers.OfType<IDisposable>()) p.Dispose();
    }
}
