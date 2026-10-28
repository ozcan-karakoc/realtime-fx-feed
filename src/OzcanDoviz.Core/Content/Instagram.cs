namespace OzcanDoviz.Core.Content;

/// <summary>Instagram'dan çekilen gönderi. Görsel adresi Instagram CDN'indedir ve bir süre sonra geçersizleşir.</summary>
public sealed record InstagramPost(string Id, string? Caption, string MediaType, string ImageUrl, string Permalink, DateTime Timestamp);

public sealed record InstagramStatus(bool HasToken, string? TokenHint, DateTime? TokenUpdatedUtc, string? UserName, DateTime? LastFetchUtc, string? LastError);

/// <summary>Instagram API'den son gönderiler. Sonuç önbelleklenir; hata olursa son başarılı liste döner.</summary>
public interface IInstagramFeed
{
    Task<IReadOnlyList<InstagramPost>> GetPostsAsync(int count, CancellationToken cancellationToken);
    Task<InstagramStatus> StatusAsync(CancellationToken cancellationToken);
    /// <summary>Anahtarı doğrular (kullanıcı adını okur) ve kaydeder.</summary>
    Task<InstagramStatus> SetTokenAsync(string token, CancellationToken cancellationToken);
    Task ClearTokenAsync(CancellationToken cancellationToken);
}

public sealed class InstagramException(string message) : Exception(message);
