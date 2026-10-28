using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

public sealed record ClientRequest([Required, MaxLength(100)] string Name, [MaxLength(1000)] string? AllowedCodes, bool IsActive = true);
public sealed record ClientView(int Id, string Name, string KeyPrefix, string? AllowedCodes, bool IsActive, DateTime CreatedUtc, DateTime? LastSeenUtc);
/// <summary>Düz anahtar yalnızca bu cevapta bir kez döner.</summary>
public sealed record ClientCreated(ClientView Client, string ApiKey, string WebSocketUrl, string RestUrl);

/// <summary>Mobil uygulama / dış site için WebSocket istemcileri ve anahtarları.</summary>
[ApiController]
[Route("api/v1/admin/clients")]
[Authorize]
public sealed class AdminClientsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ClientView>> List(CancellationToken ct) =>
        (await db.ApiClients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(View);

    [HttpPost]
    public async Task<ActionResult<ClientCreated>> Create(ClientRequest request, CancellationToken ct)
    {
        var key = ApiKeys.Generate();
        var client = new ApiClient
        {
            Name = request.Name.Trim(),
            AllowedCodes = Normalize(request.AllowedCodes),
            IsActive = request.IsActive,
            KeyPrefix = ApiKeys.Display(key),
            KeyHash = ApiKeys.Hash(key),
        };
        db.ApiClients.Add(client);
        Audit("client.create", client.Name);
        await db.SaveChangesAsync(ct);
        return Created(client, key);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClientView>> Update(int id, ClientRequest request, CancellationToken ct)
    {
        var client = await db.ApiClients.FindAsync([id], ct);
        if (client is null) return NotFound();
        client.Name = request.Name.Trim();
        client.AllowedCodes = Normalize(request.AllowedCodes);
        client.IsActive = request.IsActive;
        Audit("client.update", client.Name);
        await db.SaveChangesAsync(ct);
        return View(client);
    }

    /// <summary>Anahtarı yeniler; eski anahtar hemen geçersiz olur (açık soketler yeniden bağlanınca düşer).</summary>
    [HttpPost("{id:int}/rotate")]
    public async Task<ActionResult<ClientCreated>> Rotate(int id, CancellationToken ct)
    {
        var client = await db.ApiClients.FindAsync([id], ct);
        if (client is null) return NotFound();
        var key = ApiKeys.Generate();
        client.KeyPrefix = ApiKeys.Display(key);
        client.KeyHash = ApiKeys.Hash(key);
        Audit("client.rotate", client.Name);
        await db.SaveChangesAsync(ct);
        return Created(client, key);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var client = await db.ApiClients.FindAsync([id], ct);
        if (client is null) return NotFound();
        db.ApiClients.Remove(client);
        Audit("client.delete", client.Name);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ClientCreated Created(ApiClient client, string key)
    {
        var host = Request.Host.Value;
        var ws = (Request.IsHttps ? "wss" : "ws") + $"://{host}{MobileSocketHub.Path}?key={key}";
        var rest = $"{Request.Scheme}://{host}/api/v1/prices/client?key={key}";
        return new ClientCreated(View(client), key, ws, rest);
    }

    private void Audit(string action, string detail) =>
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = action, Detail = detail });

    private static string? Normalize(string? codes)
    {
        var set = PublicPrices.ParseCodes(codes);
        return set.Count == 0 ? null : string.Join(',', set.Order());
    }

    private static ClientView View(ApiClient c) =>
        new(c.Id, c.Name, c.KeyPrefix, c.AllowedCodes, c.IsActive, c.CreatedUtc, c.LastSeenUtc);
}
