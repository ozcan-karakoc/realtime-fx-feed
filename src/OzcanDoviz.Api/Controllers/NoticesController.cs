using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

/// <summary>Sitenin gösterdiği bildirim (kim gönderdi gibi iç alanlar yok).</summary>
public sealed record PublicNotice(string Id, string Title, string Message, string Level, string? LinkUrl, string? LinkText, DateTime CreatedUtc)
{
    public static PublicNotice From(SiteNotice n) => new(n.Id, n.Title, n.Message, n.Level, n.LinkUrl, n.LinkText, n.CreatedUtc);
}

[ApiController]
[Route("api/v1/notices")]
[AllowAnonymous]
public sealed class NoticesController(ISiteNoticeStore store) : ControllerBase
{
    /// <summary>Gösterim süresi dolmamış bildirimler: siteyi yeni açan da görür.</summary>
    [HttpGet("active")]
    public async Task<IEnumerable<PublicNotice>> Active(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return (await store.ListAsync(50, ct)).Where(n => n.IsActiveAt(now)).Select(PublicNotice.From);
    }
}

public sealed record NoticeRequest(string Title, string? Message, string Level, string? LinkUrl, string? LinkText, int DisplayMinutes);

/// <summary>Panel → Bildirim Paneli: gönder, geçmiş, geri çek, açık ekran sayısı.</summary>
[ApiController]
[Route("api/v1/admin/notices")]
[Authorize]
public sealed class AdminNoticesController(ISiteNoticeStore store, IHubContext<PriceHub> hub, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<object> List(CancellationToken ct) => new
    {
        online = PriceHub.OnlineCount,
        notices = await store.ListAsync(100, ct),
    };

    [HttpPost]
    public async Task<ActionResult<SiteNotice>> Send(NoticeRequest request, CancellationToken ct)
    {
        var notice = new SiteNotice
        {
            Title = (request.Title ?? "").Trim(),
            Message = (request.Message ?? "").Trim(),
            Level = (request.Level ?? "info").Trim().ToLowerInvariant(),
            LinkUrl = string.IsNullOrWhiteSpace(request.LinkUrl) ? null : request.LinkUrl.Trim(),
            LinkText = string.IsNullOrWhiteSpace(request.LinkText) ? null : request.LinkText.Trim(),
            DisplayMinutes = request.DisplayMinutes,
            CreatedBy = User.Identity?.Name,
        };
        var errors = SiteNoticeRules.Validate(notice);
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["notice"] = errors.ToArray() }) { Title = "Bildirim gönderilemedi." });

        await store.AddAsync(notice, ct);
        // Sitesi açık olan herkese anında.
        await hub.Clients.All.SendAsync(PriceHub.NoticeEvent, PublicNotice.From(notice), ct);
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "notice.send", Detail = $"{notice.Title} ({PriceHub.OnlineCount} açık ekran)" });
        await db.SaveChangesAsync(ct);
        return notice;
    }

    /// <summary>Geri çek: açık sayfalardan kaldırılır, yeni açanlar görmez.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Withdraw(string id, CancellationToken ct)
    {
        var notice = await store.WithdrawAsync(id, ct);
        if (notice is null) return NotFound();
        await hub.Clients.All.SendAsync(PriceHub.NoticeRemovedEvent, notice.Id, ct);
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "notice.withdraw", Detail = notice.Title });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
