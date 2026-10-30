using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

/// <summary>Sitenin herkese açık içeriği: kayan yazı, slaytlar, hakkımızda, iletişim.</summary>
[ApiController]
[Route("api/v1/content")]
[AllowAnonymous]
public sealed class ContentController(ISiteContentStore content, IFeedSettingsStore settings) : ControllerBase
{
    [HttpGet]
    public async Task<PublicContentMessage> Get(CancellationToken ct) =>
        PublicContent.From(await content.GetAsync(ct), (await settings.GetAsync(ct)).ShopName);

    /// <summary>Anasayfanın Instagram bölümü. Anahtar yoksa ya da bölüm kapalıysa boş liste.</summary>
    [HttpGet("instagram")]
    public async Task<IReadOnlyList<InstagramPost>> Instagram([FromServices] IInstagramFeed feed, CancellationToken ct)
    {
        var section = (await content.GetAsync(ct)).Instagram;
        return section.Enabled ? await feed.GetPostsAsync(section.Count, ct) : [];
    }
}

public sealed record InstagramTokenRequest(string Token);

/// <summary>Panel: Instagram erişim anahtarı. Anahtar hiçbir cevapta tam olarak geri dönmez.</summary>
[ApiController]
[Route("api/v1/admin/instagram")]
[Authorize]
public sealed class AdminInstagramController(IInstagramFeed feed, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<InstagramStatus> Status(CancellationToken ct) => feed.StatusAsync(ct);

    [HttpPut("token")]
    public async Task<ActionResult<InstagramStatus>> SetToken(InstagramTokenRequest request, CancellationToken ct)
    {
        try
        {
            var status = await feed.SetTokenAsync(request.Token, ct);
            db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "instagram.token", Detail = status.UserName });
            await db.SaveChangesAsync(ct);
            return status;
        }
        catch (Exception ex) when (ex is InstagramException or HttpRequestException)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpDelete("token")]
    public async Task<IActionResult> ClearToken(CancellationToken ct)
    {
        await feed.ClearTokenAsync(ct);
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "instagram.token.clear" });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Paneldeki önizleme: son 8 gönderi.</summary>
    [HttpGet("posts")]
    public Task<IReadOnlyList<InstagramPost>> Posts(CancellationToken ct) => feed.GetPostsAsync(8, ct);
}

/// <summary>Panelden site içeriği ve fotoğraf yönetimi.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize]
public sealed class AdminContentController(
    ISiteContentStore content,
    IMediaStorage media,
    IHubContext<PriceHub> hub,
    MobileSocketHub sockets,
    AppDbContext db) : ControllerBase
{
    [HttpGet("content")]
    public Task<SiteContent> GetContent(CancellationToken ct) => content.GetAsync(ct);

    [HttpPut("content")]
    public async Task<ActionResult<SiteContent>> SaveContent(SiteContent request, CancellationToken ct)
    {
        request.Ticker ??= new TickerSettings();
        request.Ticker.Items ??= [];
        request.Slides ??= [];
        request.About ??= new AboutSection();
        request.Contact ??= new ContactSection();
        request.Contact.Locations ??= [];
        request.Branding ??= new BrandingSection();
        request.Social ??= [];
        request.Home ??= new HomeSection();
        request.Home.Features ??= [];
        request.Home.Stats ??= [];
        request.Instagram ??= new InstagramSection();
        request.Pages ??= new PagesSection();

        var errors = SiteContentRules.Validate(request);
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["content"] = errors.ToArray() })
            {
                Title = "Site içeriği kaydedilemedi.",
            });

        var saved = await content.SaveAsync(request, User.Identity?.Name, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            User = User.Identity?.Name ?? "",
            Action = "content.save",
            Detail = $"{saved.Ticker.Items.Count} kayan yazı, {saved.Slides.Count} slayt",
        });
        await db.SaveChangesAsync(ct);

        // Açık sayfalar ve mobil istemciler içeriği yeniden çeksin.
        await hub.Clients.All.SendAsync(PriceHub.ContentEvent, ct);
        await sockets.NotifyContentChangedAsync(ct);
        return saved;
    }

    [HttpGet("media")]
    public IReadOnlyList<MediaFile> ListMedia() => media.List();

    [HttpPost("media")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<MediaFile>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null) return BadRequest(new ProblemDetails { Title = "Dosya seçilmedi." });
        try
        {
            await using var stream = file.OpenReadStream();
            var saved = await media.SaveAsync(stream, file.FileName, file.Length, ct);
            db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "media.upload", Detail = saved.Name });
            await db.SaveChangesAsync(ct);
            return saved;
        }
        catch (MediaValidationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpDelete("media/{name}")]
    public async Task<IActionResult> DeleteMedia(string name, CancellationToken ct)
    {
        var current = await content.GetAsync(ct);
        var url = "/media/" + name;
        if (current.Slides.Any(s => s.ImageUrl == url) || current.About.ImageUrl == url
            || current.Branding.HeaderLogoUrl == url || current.Branding.FooterImageUrl == url
            || current.Home.IntroImageUrl == url || current.Contact.ImageUrl == url)
            return Conflict(new ProblemDetails { Title = "Bu fotoğraf kullanılıyor; önce slayt, Hakkımızda veya logo alanından kaldırın." });
        if (!media.Delete(name)) return NotFound();
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "media.delete", Detail = name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
