using OzcanDoviz.Core.Content;

namespace OzcanDoviz.Api.Realtime;

public sealed record PublicTicker(bool Enabled, int DurationSeconds, bool ShowPrices, IReadOnlyList<string> Items);
public sealed record PublicSlide(string Id, string ImageUrl, string Title, string? Subtitle);
public sealed record PublicAbout(string Title, string Body, string? ImageUrl);
public sealed record PublicBranding(string? HeaderLogoUrl, string? FooterImageUrl);
/// <summary>Bağlantı adresi sunucuda üretilir; istemci kullanıcı girdisini href olarak kullanmaz.</summary>
public sealed record PublicSocial(SocialType Type, string Href, string Label, bool Floating);
public sealed record PublicInstagram(bool Enabled, string Title, int Count, string? ProfileUrl);
/// <summary>Şube: harita ve yol tarifi adresleri sunucuda üretilir (kullanıcı girdisi doğrudan iframe adresi olmaz).</summary>
public sealed record PublicLocation(string Name, string? Address, string? Phone, string? WorkingHours, string EmbedUrl, string DirectionsUrl);

/// <summary>Sitenin ve mobilin okuduğu içerik. Kaydeden kullanıcı adı, anahtarlar gibi iç alanlar yoktur.</summary>
public sealed record PublicContentMessage(
    string ShopName,
    string BrokerId,
    DateTime UpdatedUtc,
    PublicTicker Ticker,
    IReadOnlyList<PublicSlide> Slides,
    PublicAbout About,
    ContactSection Contact,
    PublicBranding Branding,
    IReadOnlyList<PublicSocial> Social,
    HomeSection Home,
    PublicInstagram Instagram,
    IReadOnlyList<PublicLocation> Locations,
    PagesSection Pages);

public static class PublicContent
{
    /// <param name="baseUrl">Verilirse fotoğraf yolları mutlak adrese çevrilir (mobil için).</param>
    public static PublicContentMessage From(SiteContent c, string shopName, string? baseUrl = null)
    {
        string? Abs(string? url) => url is null || baseUrl is null ? url : baseUrl.TrimEnd('/') + url;

        var social = c.Social
            .Select(s => (s, href: SiteContentRules.SocialHref(s)))
            .Where(x => x.href is not null)
            .Select(x => new PublicSocial(x.s.Type, x.href!, Label(x.s), x.s.Floating))
            .ToList();
        var instagramProfile = social.FirstOrDefault(s => s.Type == SocialType.Instagram)?.Href;

        return new PublicContentMessage(
            shopName,
            c.BrokerId,
            c.UpdatedUtc,
            new PublicTicker(
                c.Ticker.Enabled,
                c.Ticker.DurationSeconds,
                c.Ticker.ShowPrices,
                c.Ticker.Items.Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.Text)).Select(i => i.Text).ToList()),
            c.Slides.Where(s => s.Enabled).OrderBy(s => s.Order)
                .Select(s => new PublicSlide(s.Id, Abs(s.ImageUrl)!, s.Title, s.Subtitle)).ToList(),
            new PublicAbout(c.About.Title, c.About.Body, Abs(c.About.ImageUrl)),
            c.Contact,
            new PublicBranding(Abs(c.Branding.HeaderLogoUrl), Abs(c.Branding.FooterImageUrl)),
            social,
            c.Home,
            new PublicInstagram(c.Instagram.Enabled, c.Instagram.Title, c.Instagram.Count, instagramProfile),
            c.Contact.Locations
                .Where(l => !string.IsNullOrWhiteSpace(l.MapQuery))
                .Select(l => new PublicLocation(l.Name, l.Address, l.Phone, l.WorkingHours, l.EmbedUrl(), l.DirectionsUrl()))
                .ToList(),
            c.Pages);
    }

    private static string Label(SocialLink s) => s.Type switch
    {
        SocialType.WhatsApp => "WhatsApp",
        SocialType.Instagram => "Instagram",
        SocialType.Facebook => "Facebook",
        SocialType.YouTube => "YouTube",
        SocialType.Telegram => "Telegram",
        SocialType.Phone => "Telefon",
        _ => s.Type.ToString(),
    };
}
