using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Infrastructure.Social;

/// <summary>Gizli entegrasyon anahtarları: <c>App_Data/secrets.json</c>. Herkese açık içerik dosyasından ayrıdır.</summary>
public sealed class IntegrationSecrets
{
    public string? InstagramToken { get; set; }
    public DateTime? InstagramTokenUpdatedUtc { get; set; }
    public string? InstagramUserName { get; set; }
}

internal sealed class GraphMediaResponse
{
    [JsonPropertyName("data")] public List<GraphMedia>? Data { get; set; }
}

internal sealed class GraphMedia
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("caption")] public string? Caption { get; set; }
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("media_url")] public string? MediaUrl { get; set; }
    [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
    [JsonPropertyName("permalink")] public string? Permalink { get; set; }
    /// <summary>Instagram "2026-10-27T10:00:00+0000" yazar (saat diliminde iki nokta yok); metin olarak okunur.</summary>
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
}

internal sealed class GraphUser
{
    [JsonPropertyName("username")] public string? UserName { get; set; }
}

internal sealed class GraphRefresh
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
}

/// <summary>
/// Instagram API (Instagram ile Giriş, işletme/içerik üreticisi hesabı) üzerinden son gönderiler.
/// Uzun ömürlü anahtar 60 gün geçerlidir; 30 günden eski anahtar okuma sırasında yenilenir.
/// Liste 15 dakika önbelleklenir; API hata verirse son başarılı liste gösterilmeye devam eder.
/// </summary>
internal sealed class InstagramFeed : IInstagramFeed
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    private readonly HttpClient _http;
    private readonly ILogger<InstagramFeed> _logger;
    private readonly JsonFileStore<IntegrationSecrets> _secrets;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<InstagramPost> _posts = [];
    private DateTime? _lastFetchUtc;
    private string? _lastError;

    public InstagramFeed(HttpClient http, IOptions<StorageOptions> options, IHostEnvironment env, ILogger<InstagramFeed> logger)
    {
        _http = http;
        _logger = logger;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _secrets = new JsonFileStore<IntegrationSecrets>(dir, "secrets.json", keepHistory: 0, () => new IntegrationSecrets(), logger);
    }

    public async Task<IReadOnlyList<InstagramPost>> GetPostsAsync(int count, CancellationToken cancellationToken)
    {
        if (_lastFetchUtc is { } at && DateTime.UtcNow - at < CacheFor) return _posts.Take(count).ToList();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_lastFetchUtc is { } again && DateTime.UtcNow - again < CacheFor) return _posts.Take(count).ToList();
            var secrets = await _secrets.ReadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(secrets.InstagramToken)) return [];

            _lastFetchUtc = DateTime.UtcNow; // hata olsa da 15 dk içinde tekrar denenmez
            try
            {
                await RefreshIfOldAsync(secrets, cancellationToken);
                _posts = await FetchAsync(secrets.InstagramToken!, cancellationToken);
                _lastError = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or InstagramException or JsonException
                                       || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _lastError = ex.Message;
                _logger.LogWarning("Instagram gönderileri alınamadı: {Error}", ex.Message);
            }
            return _posts.Take(count).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InstagramStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var s = await _secrets.ReadAsync(cancellationToken);
        return new InstagramStatus(
            !string.IsNullOrWhiteSpace(s.InstagramToken),
            Hint(s.InstagramToken),
            s.InstagramTokenUpdatedUtc,
            s.InstagramUserName,
            _lastFetchUtc,
            _lastError);
    }

    public async Task<InstagramStatus> SetTokenAsync(string token, CancellationToken cancellationToken)
    {
        token = (token ?? "").Trim();
        if (token.Length < 20) throw new InstagramException("Erişim anahtarı geçersiz görünüyor.");

        var user = await GetAsync<GraphUser>($"me?fields=username&access_token={Uri.EscapeDataString(token)}", cancellationToken);
        await _secrets.WriteAsync(new IntegrationSecrets
        {
            InstagramToken = token,
            InstagramTokenUpdatedUtc = DateTime.UtcNow,
            InstagramUserName = user.UserName,
        }, cancellationToken);
        _lastFetchUtc = null;
        _lastError = null;
        _posts = [];
        return await StatusAsync(cancellationToken);
    }

    public async Task ClearTokenAsync(CancellationToken cancellationToken)
    {
        await _secrets.WriteAsync(new IntegrationSecrets(), cancellationToken);
        _posts = [];
        _lastFetchUtc = null;
        _lastError = null;
    }

    private async Task<IReadOnlyList<InstagramPost>> FetchAsync(string token, CancellationToken ct)
    {
        var body = await GetAsync<GraphMediaResponse>(
            "me/media?fields=id,caption,media_type,media_url,thumbnail_url,permalink,timestamp&limit=24&access_token="
            + Uri.EscapeDataString(token), ct);

        return (body.Data ?? [])
            .Select(m => new
            {
                m,
                // Video için kapak görseli; görsel/albüm için media_url.
                image = m.MediaType == "VIDEO" ? m.ThumbnailUrl : m.MediaUrl,
            })
            .Where(x => x.m.Id is not null && IsHttps(x.image) && IsHttps(x.m.Permalink))
            .Select(x => new InstagramPost(
                x.m.Id!,
                x.m.Caption is { Length: > 300 } c ? c[..300] + "…" : x.m.Caption,
                x.m.MediaType ?? "IMAGE",
                x.image!,
                x.m.Permalink!,
                ParseTime(x.m.Timestamp)))
            .ToList();
    }

    internal static DateTime ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
        // "+0000" → "+00:00"
        if (value.Length > 5 && value[^5] is '+' or '-' && value[^4..].All(char.IsDigit))
            value = value[..^2] + ":" + value[^2..];
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime
            : DateTime.MinValue;
    }

    private async Task RefreshIfOldAsync(IntegrationSecrets s, CancellationToken ct)
    {
        if (s.InstagramTokenUpdatedUtc is { } at && DateTime.UtcNow - at < RefreshAfter) return;
        try
        {
            var r = await GetAsync<GraphRefresh>(
                "refresh_access_token?grant_type=ig_refresh_token&access_token=" + Uri.EscapeDataString(s.InstagramToken!), ct);
            if (string.IsNullOrWhiteSpace(r.AccessToken)) return;
            s.InstagramToken = r.AccessToken;
            s.InstagramTokenUpdatedUtc = DateTime.UtcNow;
            await _secrets.WriteAsync(s, ct);
            _logger.LogInformation("Instagram erişim anahtarı yenilendi");
        }
        catch (Exception ex) when (ex is HttpRequestException or InstagramException)
        {
            // Yenileme başarısızsa mevcut anahtarla devam edilir; süresi dolunca okuma hata verir ve panelde görünür.
            _logger.LogWarning("Instagram anahtarı yenilenemedi: {Error}", ex.Message);
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Hata gövdesi anahtarı içerebilir; günlüğe/arayüze yalnızca durum kodu yazılır.
            var hint = (int)response.StatusCode is 400 or 401 or 403 ? " (anahtar geçersiz veya süresi dolmuş olabilir)" : "";
            throw new InstagramException($"Instagram {(int)response.StatusCode} döndü{hint}.");
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new InstagramException("Instagram boş cevap döndürdü.");
        }
        catch (JsonException)
        {
            throw new InstagramException("Instagram beklenmeyen bir cevap döndürdü.");
        }
    }

    private static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    private static string? Hint(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token[..Math.Min(6, token.Length)] + "…" + token[^4..];
}
