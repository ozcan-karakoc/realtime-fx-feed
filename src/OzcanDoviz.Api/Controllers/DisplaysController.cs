using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Core.Displays;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

public sealed record FramePreview(string Text, string Hex, int Length);

/// <summary>
/// Ekran sayfası ve göndericinin okuduğu uçlar. Ekran tanımı gizli değildir (fiyatlar zaten herkese açık);
/// kapalı ekran 404 döner.
/// </summary>
[ApiController]
[Route("api/v1/displays")]
[AllowAnonymous]
public sealed class DisplaysController(IDisplayStore store, IPriceBoard board) : ControllerBase
{
    private static readonly TimeZoneInfo Turkey = FindTurkey();

    [HttpGet("{id}")]
    public async Task<ActionResult<DisplayScreen>> Get(string id, CancellationToken ct)
    {
        var screen = await store.FindAsync(id, ct);
        return screen is { Enabled: true } ? screen : NotFound(new ProblemDetails { Title = "Ekran bulunamadı veya kapalı." });
    }

    /// <summary>COM/Bluetooth çerçevesi: ham baytlar (application/octet-stream). Göndericiler olduğu gibi yazar.</summary>
    [HttpGet("{id}/frame")]
    public async Task<IActionResult> Frame(string id, CancellationToken ct)
    {
        var screen = await store.FindAsync(id, ct);
        if (screen is not { Enabled: true }) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return File(DisplayFrame.Build(screen, board.Current.Items, Now()), "application/octet-stream");
    }

    /// <summary>Panelde önizleme: çerçevenin metni ve onaltılık dökümü.</summary>
    [HttpGet("{id}/frame/preview")]
    public async Task<ActionResult<FramePreview>> Preview(string id, CancellationToken ct)
    {
        var screen = await store.FindAsync(id, ct);
        if (screen is null) return NotFound();
        return Preview(screen);
    }

    internal FramePreview Preview(DisplayScreen screen)
    {
        var bytes = DisplayFrame.Build(screen, board.Current.Items, Now());
        return new FramePreview(DisplayFrame.BuildText(screen, board.Current.Items, Now()), Convert.ToHexString(bytes), bytes.Length);
    }

    internal static DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Turkey);

    private static TimeZoneInfo FindTurkey()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }
}

/// <summary>Panel: ekran tanımları.</summary>
[ApiController]
[Route("api/v1/admin/displays")]
[Authorize]
public sealed class AdminDisplaysController(IDisplayStore store, IPriceBoard board, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DisplayScreen>> List(CancellationToken ct) => store.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<DisplayScreen>> Create(DisplayScreen screen, CancellationToken ct)
    {
        if (await store.FindAsync(screen.Id ?? "", ct) is not null)
            return Conflict(new ProblemDetails { Title = "Bu kimlikte bir ekran zaten var." });
        return await SaveAsync(screen, "display.create", ct);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DisplayScreen>> Update(string id, DisplayScreen screen, CancellationToken ct)
    {
        if (await store.FindAsync(id, ct) is null) return NotFound();
        if (!id.Equals(screen.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Ekran kimliği değiştirilemez; yeni ekran oluşturun." });
        return await SaveAsync(screen, "display.update", ct);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!await store.DeleteAsync(id, ct)) return NotFound();
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "display.delete", Detail = id });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Kaydetmeden önizleme: formdaki ayarlarla çerçeve metni ve baytları.</summary>
    [HttpPost("preview")]
    public ActionResult<FramePreview> Preview(DisplayScreen screen)
    {
        Normalize(screen);
        var errors = DisplayRules.Validate(screen).Where(e => !e.StartsWith("Ekran", StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["screen"] = errors }));
        var bytes = DisplayFrame.Build(screen, board.Current.Items, DisplaysController.Now());
        return new FramePreview(DisplayFrame.BuildText(screen, board.Current.Items, DisplaysController.Now()), Convert.ToHexString(bytes), bytes.Length);
    }

    private async Task<ActionResult<DisplayScreen>> SaveAsync(DisplayScreen screen, string action, CancellationToken ct)
    {
        Normalize(screen);
        var errors = DisplayRules.Validate(screen);
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["screen"] = errors.ToArray() })
            {
                Title = "Ekran kaydedilemedi.",
            });
        var saved = await store.SaveAsync(screen, ct);
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = action, Detail = $"{saved.Id} ({saved.Output})" });
        await db.SaveChangesAsync(ct);
        return saved;
    }

    private static void Normalize(DisplayScreen s)
    {
        s.Id = (s.Id ?? "").Trim().ToLowerInvariant();
        s.Name = (s.Name ?? "").Trim();
        s.Layout ??= new DisplayLayout();
        s.Layout.ProductCodes ??= [];
        s.Serial ??= new SerialSettings();
        s.Bluetooth ??= new BluetoothSettings();
        s.Serial.HeaderTemplate ??= "";
        s.Serial.PrefixHex ??= "";
        s.Serial.SuffixHex ??= "";
        s.Serial.LineSeparator ??= "\\r\\n";
    }
}
