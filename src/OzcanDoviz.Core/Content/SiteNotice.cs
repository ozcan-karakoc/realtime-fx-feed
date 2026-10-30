namespace OzcanDoviz.Core.Content;

/// <summary>
/// Panelden siteye gönderilen bildirim. Gönderildiği anda açık olan bütün sayfalara SignalR ile düşer.
/// <see cref="DisplayMinutes"/> sıfırdan büyükse bu süre içinde siteyi yeni açanlar da görür.
/// </summary>
public sealed class SiteNotice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>info, success, warning, danger</summary>
    public string Level { get; set; } = "info";
    /// <summary>Site içi yol (/canli-piyasalar) veya https adresi; isteğe bağlı.</summary>
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public int DisplayMinutes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    /// <summary>Panelden geri çekildiyse; açık sayfalardan da kaldırılır.</summary>
    public DateTime? WithdrawnUtc { get; set; }

    public bool IsActiveAt(DateTime nowUtc) =>
        WithdrawnUtc is null && DisplayMinutes > 0 && nowUtc < CreatedUtc.AddMinutes(DisplayMinutes);
}

public interface ISiteNoticeStore
{
    Task<IReadOnlyList<SiteNotice>> ListAsync(int take, CancellationToken cancellationToken);
    Task<SiteNotice> AddAsync(SiteNotice notice, CancellationToken cancellationToken);
    Task<SiteNotice?> WithdrawAsync(string id, CancellationToken cancellationToken);
}

public static class SiteNoticeRules
{
    private static readonly string[] Levels = ["info", "success", "warning", "danger"];

    public static IReadOnlyList<string> Validate(SiteNotice n)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(n.Title) || n.Title.Length > 80) e.Add("Başlık 1-80 karakter olmalı.");
        if ((n.Message ?? "").Length > 500) e.Add("Mesaj en fazla 500 karakter olabilir.");
        if (!Levels.Contains(n.Level)) e.Add("Tür info, success, warning veya danger olmalı.");
        if (n.DisplayMinutes is < 0 or > 1440) e.Add("Gösterim süresi 0-1440 dakika olmalı.");
        if (!string.IsNullOrWhiteSpace(n.LinkUrl) && !IsSafeLink(n.LinkUrl)) e.Add("Bağlantı site içi bir yol (/…) veya https adresi olmalı.");
        if ((n.LinkText ?? "").Length > 40) e.Add("Bağlantı metni en fazla 40 karakter olabilir.");
        return e;
    }

    public static bool IsSafeLink(string url) =>
        (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
        || (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps);
}
