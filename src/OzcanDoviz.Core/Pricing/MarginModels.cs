namespace OzcanDoviz.Core.Pricing;

/// <summary>
/// Marj biçimi. <see cref="Multiply"/> milyem/katsayı alanıdır (ör. 22 ayar alış = HAS alış × 0,9120);
/// <see cref="Add"/> TL ekler (eksi değer düşer), <see cref="Percent"/> yüzde ekler.
/// </summary>
public enum MarginMode { Add, Percent, Multiply }

public sealed class MarginSide
{
    public MarginMode Mode { get; set; } = MarginMode.Add;
    public decimal Value { get; set; }
}

public sealed class ProductRule
{
    /// <summary>Ekranda ve sokette görünen ürün kodu (benzersiz).</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>altin, doviz, parite, maden</summary>
    public string Category { get; set; } = "altin";
    /// <summary>Sağlayıcıdaki kaynak sembol (ör. HASALTIN, USDTRY).</summary>
    public string SourceSymbol { get; set; } = "";
    public MarginSide Buy { get; set; } = new();
    public MarginSide Sell { get; set; } = new();
    public int Decimals { get; set; } = 2;
    public bool Visible { get; set; } = true;
    public int Order { get; set; }
}

/// <summary>data/margins.json içeriği.</summary>
public sealed class MarginDocument
{
    public int Version { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public List<ProductRule> Products { get; set; } = [];
}

/// <summary>data/settings.json içeriği: hangi sağlayıcı, ne sıklıkla.</summary>
public sealed class FeedSettings
{
    public string ActiveProvider { get; set; } = "harem";
    /// <summary>Etkin sağlayıcı hata verirse kullanılacak ikinci sağlayıcı (boş olabilir).</summary>
    public string? FallbackProvider { get; set; } = "saglamoglu";
    /// <summary>HTTP sağlayıcılar için yoklama aralığı (saniye).</summary>
    public int PollSeconds { get; set; } = 5;
    public string ShopName { get; set; } = "Özcan Döviz";
}

public interface IMarginStore
{
    Task<MarginDocument> GetAsync(CancellationToken cancellationToken);
    Task<MarginDocument> SaveAsync(MarginDocument document, string? user, CancellationToken cancellationToken);
}

public interface IFeedSettingsStore
{
    Task<FeedSettings> GetAsync(CancellationToken cancellationToken);
    Task<FeedSettings> SaveAsync(FeedSettings settings, CancellationToken cancellationToken);
}
