using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Infrastructure.Persistence;
using OzcanDoviz.Infrastructure.Pricing;
using OzcanDoviz.Infrastructure.Providers;

namespace OzcanDoviz.Api.Controllers;

/// <summary>
/// Panelden eklenen fiyat sağlayıcıları (ws://, wss://, http(s)://). Yalnızca yönetici; sunucu girilen adrese bağlanır,
/// bu yüzden her işlem işlem izine yazılır.
/// </summary>
[ApiController]
[Route("api/v1/admin/custom-providers")]
[Authorize]
public sealed class AdminCustomProvidersController(
    ICustomProviderService service,
    ProviderRegistry registry,
    IFeedSettingsStore settings,
    IMarginStore margins,
    PriceRefreshSignal refresh,
    AppDbContext db) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<CustomProviderDefinition> List() => service.List();

    /// <summary>Kaydetmeden dener: bağlanır, ilk fiyatlı mesajı bekler (en fazla 10 sn), örnek mesajı ve sembolleri döner.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<CustomProviderTestResult>> Test(CustomProviderDefinition definition, CancellationToken ct)
    {
        Normalize(definition);
        // Anahtar denemede gerekmez; adres ve eşleme doğrulanır.
        var errors = CustomProviderRules.Validate(definition, []).Where(e => !e.StartsWith("Anahtar", StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0) return Invalid(errors);

        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "custom-provider.test", Detail = definition.Url });
        await db.SaveChangesAsync(ct);
        return await service.TestAsync(definition, ct);
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<CustomProviderDefinition>> Save(string key, CustomProviderDefinition definition, CancellationToken ct)
    {
        Normalize(definition);
        if (!key.Equals(definition.Key, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Adresteki anahtar ile gövdedeki anahtar aynı olmalı." });
        var errors = CustomProviderRules.Validate(definition, registry.BuiltIn.Select(p => p.Key));
        if (errors.Count > 0) return Invalid(errors.ToArray());

        var saved = await service.SaveAsync(definition, ct);
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "custom-provider.save", Detail = $"{saved.Key} → {saved.Url}" });
        await db.SaveChangesAsync(ct);
        refresh.Request();
        return saved;
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        var usage = await UsageAsync(key, ct);
        if (usage.Count > 0)
            return Conflict(new ProblemDetails { Title = $"Bu sağlayıcı kullanılıyor: {string.Join(", ", usage)}. Önce oradan kaldırın." });
        if (!await service.DeleteAsync(key, ct)) return NotFound();
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = "custom-provider.delete", Detail = key });
        await db.SaveChangesAsync(ct);
        refresh.Request();
        return NoContent();
    }

    private async Task<List<string>> UsageAsync(string key, CancellationToken ct)
    {
        bool Is(string? v) => key.Equals(v, StringComparison.OrdinalIgnoreCase);
        var s = await settings.GetAsync(ct);
        var used = new List<string>();
        if (Is(s.ActiveProvider)) used.Add("genel sağlayıcı");
        if (Is(s.FallbackProvider)) used.Add("yedek sağlayıcı");
        used.AddRange(s.CategoryProviders.Where(kv => Is(kv.Value)).Select(kv => $"{kv.Key} kategorisi"));
        used.AddRange((await margins.GetAsync(ct)).Products.Where(p => Is(p.Provider)).Select(p => $"{p.Name} ürünü"));
        return used;
    }

    private ActionResult Invalid(string[] errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["provider"] = errors })
        {
            Title = "Sağlayıcı ayarları geçersiz.",
        });

    private static void Normalize(CustomProviderDefinition d)
    {
        d.Key = (d.Key ?? "").Trim().ToLowerInvariant();
        d.Name = (d.Name ?? "").Trim();
        d.Url = (d.Url ?? "").Trim();
        d.Protocol = string.IsNullOrWhiteSpace(d.Protocol) ? "auto" : d.Protocol.Trim().ToLowerInvariant();
        d.DataPath = (d.DataPath ?? "").Trim();
        d.SymbolField = (d.SymbolField ?? "").Trim();
        d.BuyField = (d.BuyField ?? "").Trim();
        d.SellField = (d.SellField ?? "").Trim();
        d.NameField = string.IsNullOrWhiteSpace(d.NameField) ? null : d.NameField.Trim();
        d.Origin = string.IsNullOrWhiteSpace(d.Origin) ? null : d.Origin.Trim();
        d.SubscribeMessage = string.IsNullOrWhiteSpace(d.SubscribeMessage) ? null : d.SubscribeMessage;
    }
}
