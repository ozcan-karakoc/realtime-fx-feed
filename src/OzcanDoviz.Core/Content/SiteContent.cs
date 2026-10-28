namespace OzcanDoviz.Core.Content;

/// <summary>
/// Sitenin ve mobil uygulamanın düzenlenebilir içeriği: kayan yazı, slayt fotoğrafları, Hakkımızda, İletişim.
/// <c>App_Data/content.json</c> dosyasında durur; fotoğraflar <c>App_Data/uploads</c> altındadır.
/// </summary>
public sealed class SiteContent
{
    /// <summary>Mobil servisin aracı kimliği: <c>/api/v1/mobile/{brokerId}/…</c>. Küçük harf, rakam ve tire.</summary>
    public string BrokerId { get; set; } = "ozcan-doviz";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public TickerSettings Ticker { get; set; } = new();
    public List<Slide> Slides { get; set; } = [];
    public AboutSection About { get; set; } = new();
    public ContactSection Contact { get; set; } = new();
    public BrandingSection Branding { get; set; } = new();
    public List<SocialLink> Social { get; set; } = [];
    /// <summary>Dosyada henüz yoksa demo tanıtım metinleriyle başlar; panelden değiştirilir.</summary>
    public HomeSection Home { get; set; } = HomeSection.Demo();
    public InstagramSection Instagram { get; set; } = new();
}

/// <summary>Header logosu ve footer görseli (yüklenen fotoğraflardan).</summary>
public sealed class BrandingSection
{
    public string? HeaderLogoUrl { get; set; }
    /// <summary>Footer'da logo/kurumsal görsel.</summary>
    public string? FooterImageUrl { get; set; }
}

public enum SocialType { WhatsApp, Instagram, Facebook, YouTube, Telegram, Phone }

/// <summary>
/// Sosyal medya / iletişim bağlantısı. <see cref="Value"/> türe göre: WhatsApp ve Telefon için numara,
/// Instagram ve Telegram için kullanıcı adı, Facebook ve YouTube için https adresi.
/// </summary>
public sealed class SocialLink
{
    public SocialType Type { get; set; }
    public string Value { get; set; } = "";
    /// <summary>Sağ altta, sohbet düğmesinin üstünde yuvarlak ikon olarak gösterilsin.</summary>
    public bool Floating { get; set; } = true;
    /// <summary>WhatsApp için hazır mesaj (isteğe bağlı).</summary>
    public string? Message { get; set; }
}

/// <summary>Anasayfanın tanıtım bölümleri.</summary>
public sealed class HomeSection
{
    public string IntroTitle { get; set; } = "";
    public string IntroText { get; set; } = "";
    public List<HomeFeature> Features { get; set; } = [];
    public List<HomeStat> Stats { get; set; } = [];
    public string CtaTitle { get; set; } = "";
    public string CtaText { get; set; } = "";

    /// <summary>Firma tanıtımı için demo metinler. Yayına almadan önce gerçek bilgilerle değiştirilmeli.</summary>
    public static HomeSection Demo() => new()
    {
        IntroTitle = "Altın ve dövizde güvenilir adres",
        IntroText = "Yılların tecrübesiyle altın ve döviz alım satımında şeffaf fiyat, hızlı işlem ve güler yüzlü hizmet sunuyoruz. "
                    + "Fiyatlarımız piyasayla anlık güncellenir; dilediğiniz fiyatı asistanımızdan sabitleyip dükkanımızda işleminizi tamamlayabilirsiniz.",
        Features =
        [
            new() { Icon = "⚡", Title = "Anlık fiyat", Text = "Altın ve döviz fiyatları piyasayla saniyeler içinde güncellenir." },
            new() { Icon = "🔒", Title = "Kur sabitleme", Text = "Asistan üzerinden fiyatınızı belirli bir süre için sabitleyin, referans numaranızla gelin." },
            new() { Icon = "🛡", Title = "Güvenli işlem", Text = "Faturalı, kayıtlı ve şeffaf alım satım." },
            new() { Icon = "🤝", Title = "Uzman kadro", Text = "Ziynet, gram ve yatırım altınında doğru yönlendirme." },
        ],
        Stats =
        [
            new() { Value = "25+", Label = "Yıllık tecrübe" },
            new() { Value = "10.000+", Label = "Mutlu müşteri" },
            new() { Value = "7/24", Label = "Canlı fiyat" },
        ],
        CtaTitle = "Fiyat sabitlemek ister misiniz?",
        CtaText = "Sağ alttaki asistanı açın, ürünü seçin; fiyatınız dakikalarca sabit kalsın.",
    };
}

public sealed class HomeFeature
{
    /// <summary>Kısa simge (emoji veya tek karakter).</summary>
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class HomeStat
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>Anasayfadaki Instagram bölümü. Erişim anahtarı burada değil, ayrı gizli dosyadadır.</summary>
public sealed class InstagramSection
{
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "Instagram'dan son paylaşımlar";
    public int Count { get; set; } = 8;
}

public sealed class TickerSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Bir turun saniyesi; büyük değer daha yavaş kayar.</summary>
    public int DurationSeconds { get; set; } = 40;
    /// <summary>Kayan yazıya canlı fiyatları da ekle (USD, EUR, HAS…).</summary>
    public bool ShowPrices { get; set; } = true;
    public List<TickerItem> Items { get; set; } = [];
}

public sealed class TickerItem
{
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Slide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Yüklenen fotoğrafın yolu (<c>/media/…</c>).</summary>
    public string ImageUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
}

public sealed class AboutSection
{
    public string Title { get; set; } = "Hakkımızda";
    /// <summary>Düz metin; boş satır paragraf ayırır. HTML kabul edilmez.</summary>
    public string Body { get; set; } = "";
    public string? ImageUrl { get; set; }
}

public sealed class ContactSection
{
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? WorkingHours { get; set; }
    /// <summary>Yalnızca <c>https://www.google.com/maps/embed?…</c> adresi kabul edilir.</summary>
    public string? MapEmbedUrl { get; set; }
}

public interface ISiteContentStore
{
    Task<SiteContent> GetAsync(CancellationToken cancellationToken);
    Task<SiteContent> SaveAsync(SiteContent content, string? user, CancellationToken cancellationToken);
}

public static class SiteContentRules
{
    public const string MapEmbedPrefix = "https://www.google.com/maps/embed";

    public static IReadOnlyList<string> Validate(SiteContent c)
    {
        var errors = new List<string>();
        if (!IsValidBrokerId(c.BrokerId))
            errors.Add("Aracı kimliği 3-40 karakter olmalı; yalnızca küçük harf, rakam ve tire.");
        if (c.Ticker.DurationSeconds is < 5 or > 600)
            errors.Add("Kayan yazı süresi 5-600 saniye arası olmalı.");
        if (c.Ticker.Items.Count > 50) errors.Add("En fazla 50 kayan yazı olabilir.");
        if (c.Ticker.Items.Any(i => (i.Text ?? "").Length > 300)) errors.Add("Kayan yazı en fazla 300 karakter olabilir.");
        if (c.Slides.Count > 20) errors.Add("En fazla 20 slayt olabilir.");
        foreach (var s in c.Slides)
        {
            if (!IsMediaUrl(s.ImageUrl)) errors.Add($"Slayt '{s.Title}': fotoğraf yüklenmeli.");
            if ((s.Title ?? "").Length > 120 || (s.Subtitle ?? "").Length > 240) errors.Add($"Slayt '{s.Title}': başlık çok uzun.");
        }
        if (c.About.Body.Length > 10_000) errors.Add("Hakkımızda metni en fazla 10.000 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(c.About.ImageUrl) && !IsMediaUrl(c.About.ImageUrl))
            errors.Add("Hakkımızda fotoğrafı geçersiz.");
        if (!string.IsNullOrWhiteSpace(c.Contact.MapEmbedUrl)
            && !c.Contact.MapEmbedUrl.StartsWith(MapEmbedPrefix, StringComparison.Ordinal))
            errors.Add($"Harita adresi {MapEmbedPrefix} ile başlamalı (Google Haritalar → Paylaş → Haritayı yerleştir).");
        if (!string.IsNullOrWhiteSpace(c.Contact.Email) && !c.Contact.Email.Contains('@'))
            errors.Add("E-posta geçersiz.");

        foreach (var (url, label) in new[] { (c.Branding.HeaderLogoUrl, "Header logosu"), (c.Branding.FooterImageUrl, "Footer görseli") })
            if (!string.IsNullOrWhiteSpace(url) && !IsMediaUrl(url)) errors.Add($"{label} geçersiz.");

        if (c.Social.Count > 12) errors.Add("En fazla 12 sosyal bağlantı olabilir.");
        foreach (var s in c.Social)
            if (SocialHref(s) is null) errors.Add($"{s.Type}: '{s.Value}' geçersiz.");

        if (c.Home.Features.Count > 8) errors.Add("Anasayfada en fazla 8 özellik kartı olabilir.");
        if (c.Home.Stats.Count > 6) errors.Add("Anasayfada en fazla 6 sayı kutusu olabilir.");
        if ((c.Home.IntroText ?? "").Length > 2000 || (c.Home.CtaText ?? "").Length > 1000) errors.Add("Anasayfa metni çok uzun.");
        if (c.Instagram.Count is < 1 or > 24) errors.Add("Instagram gönderi sayısı 1-24 arası olmalı.");
        return errors;
    }

    /// <summary>Bağlantının tıklanabilir adresi; değer geçersizse null. Kullanıcı girdisi asla doğrudan href olmaz.</summary>
    public static string? SocialHref(SocialLink s)
    {
        var v = (s.Value ?? "").Trim();
        string Digits() => new(v.Where(char.IsDigit).ToArray());
        static bool Handle(string h) => h.Length is >= 1 and <= 30 && h.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_');

        switch (s.Type)
        {
            case SocialType.WhatsApp:
            {
                var d = Digits();
                if (d.Length is < 10 or > 15) return null;
                var text = string.IsNullOrWhiteSpace(s.Message) ? "" : "?text=" + Uri.EscapeDataString(s.Message.Trim());
                return $"https://wa.me/{d}{text}";
            }
            case SocialType.Phone:
            {
                var d = Digits();
                return d.Length is < 7 or > 15 ? null : $"tel:{(v.StartsWith('+') ? "+" : "")}{d}";
            }
            case SocialType.Instagram:
            {
                var h = UserName(v, "instagram.com");
                return h is not null && Handle(h) ? $"https://www.instagram.com/{h}/" : null;
            }
            case SocialType.Telegram:
            {
                var h = UserName(v, "t.me");
                return h is not null && Handle(h) ? $"https://t.me/{h}" : null;
            }
            case SocialType.Facebook:
                return IsHttpsOn(v, "facebook.com") ? v : null;
            case SocialType.YouTube:
                return IsHttpsOn(v, "youtube.com") || IsHttpsOn(v, "youtu.be") ? v : null;
            default:
                return null;
        }
    }

    /// <summary>"@kullanici", "kullanici" veya "https://instagram.com/kullanici" → "kullanici".</summary>
    private static string? UserName(string v, string host)
    {
        v = v.TrimStart('@');
        if (Uri.TryCreate(v, UriKind.Absolute, out var u))
        {
            if (u.Scheme != Uri.UriSchemeHttps || !(u.Host == host || u.Host.EndsWith("." + host))) return null;
            return u.AbsolutePath.Trim('/').Split('/')[0];
        }
        return v;
    }

    private static bool IsHttpsOn(string v, string host) =>
        Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == host || u.Host.EndsWith("." + host));

    public static bool IsValidBrokerId(string? id) =>
        id is { Length: >= 3 and <= 40 } && id.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Slayt/hakkımızda fotoğrafı yalnızca bu sitenin yüklediği dosya olabilir.</summary>
    public static bool IsMediaUrl(string? url) =>
        url is not null && url.StartsWith("/media/", StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal);
}
