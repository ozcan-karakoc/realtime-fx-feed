using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Locks;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;
using OzcanDoviz.Infrastructure.Pricing;

namespace OzcanDoviz.Api.Controllers;

public sealed record LockConfig(bool Enabled, int Minutes, decimal MaxQuantity);

/// <summary>Müşteriye dönen sabitleme. Telefon ve ad kısaltılmıştır (referansı bilen herkes sorgulayabilir).</summary>
public sealed record PublicLock(
    string Ref,
    string ProductCode,
    string ProductName,
    LockSide Side,
    decimal UnitPrice,
    decimal Quantity,
    decimal Total,
    int Decimals,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    LockStatus Status,
    string Customer)
{
    public static PublicLock From(RateLock l) => new(
        l.Ref, l.ProductCode, l.ProductName, l.Side, l.UnitPrice, l.Quantity, l.Total, l.Decimals,
        l.CreatedUtc, l.ExpiresUtc, l.StatusAt(DateTime.UtcNow), Mask(l.CustomerName));

    private static string Mask(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0] + new string('*', Math.Max(1, p.Length - 1))));
}

/// <summary>Sitedeki mesaj botunun kur sabitleme uçları.</summary>
[ApiController]
[Route("api/v1/locks")]
[AllowAnonymous]
public sealed class LocksController(IRateLockService service, IRateLockStore store, IFeedSettingsStore settings) : ControllerBase
{
    [HttpGet("config")]
    public async Task<LockConfig> Config(CancellationToken ct)
    {
        var o = (await settings.GetAsync(ct)).RateLock;
        return new LockConfig(o.Enabled, o.Minutes, o.MaxQuantity);
    }

    [HttpPost]
    [EnableRateLimiting("lock")]
    public async Task<ActionResult<PublicLock>> Create(RateLockRequest request, CancellationToken ct)
    {
        try
        {
            var created = await service.CreateAsync(request, ct);
            return PublicLock.From(created);
        }
        catch (RateLockException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpGet("{reference}")]
    public async Task<ActionResult<PublicLock>> Find(string reference, CancellationToken ct)
    {
        var item = await store.FindAsync(reference, ct);
        return item is null ? NotFound(new ProblemDetails { Title = "Referans bulunamadı." }) : PublicLock.From(item);
    }
}

public sealed record AdminLock(RateLock Lock, LockStatus Status);

/// <summary>Panel: sabitleme listesi, tamamla/iptal, ayarlar.</summary>
[ApiController]
[Route("api/v1/admin/locks")]
[Authorize]
public sealed class AdminLocksController(IRateLockStore store, IFeedSettingsStore settings, IHubContext<PriceHub> hub, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<AdminLock>> List([FromQuery] int take = 200, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return (await store.ListAsync(Math.Clamp(take, 1, 2000), ct)).Select(l => new AdminLock(l, l.StatusAt(now)));
    }

    [HttpPost("{reference}/complete")]
    public Task<ActionResult<AdminLock>> Complete(string reference, CancellationToken ct) => Close(reference, LockStatus.Completed, ct);

    [HttpPost("{reference}/cancel")]
    public Task<ActionResult<AdminLock>> Cancel(string reference, CancellationToken ct) => Close(reference, LockStatus.Cancelled, ct);

    [HttpGet("settings")]
    public async Task<RateLockSettings> GetSettings(CancellationToken ct) => (await settings.GetAsync(ct)).RateLock;

    [HttpPut("settings")]
    public async Task<ActionResult<RateLockSettings>> SaveSettings(RateLockSettings request, [FromServices] PriceRefreshSignal refresh, CancellationToken ct)
    {
        if (request.Minutes is < 1 or > 240)
            return BadRequest(new ProblemDetails { Title = "Süre 1-240 dakika arası olmalı." });
        if (request.MaxQuantity <= 0)
            return BadRequest(new ProblemDetails { Title = "Azami miktar sıfırdan büyük olmalı." });

        var current = await settings.GetAsync(ct);
        current.RateLock = request;
        var saved = await settings.SaveAsync(current, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            User = User.Identity?.Name ?? "",
            Action = "locks.settings",
            Detail = $"{(request.Enabled ? "açık" : "kapalı")}, {request.Minutes} dk, en fazla {request.MaxQuantity}",
        });
        await db.SaveChangesAsync(ct);
        refresh.Request();
        return saved.RateLock;
    }

    private async Task<ActionResult<AdminLock>> Close(string reference, LockStatus status, CancellationToken ct)
    {
        var existing = await store.FindAsync(reference, ct);
        if (existing is null) return NotFound();
        if (existing.ClosedStatus is not null)
            return Conflict(new ProblemDetails { Title = "Bu sabitleme zaten kapatılmış." });

        var item = await store.CloseAsync(reference, status, User.Identity?.Name ?? "", ct);
        db.AuditEntries.Add(new AuditEntry
        {
            User = User.Identity?.Name ?? "",
            Action = status == LockStatus.Completed ? "lock.complete" : "lock.cancel",
            Detail = $"{item!.Ref} {item.ProductCode} {item.Quantity} × {item.UnitPrice}",
        });
        await db.SaveChangesAsync(ct);

        // Müşteri sayfası açıksa sonucu anında görsün.
        await hub.Clients.Group(LockGroups.Name(item.Ref)).SendAsync(PriceHub.LockEvent, PublicLock.From(item), ct);
        return new AdminLock(item, item.StatusAt(DateTime.UtcNow));
    }
}
