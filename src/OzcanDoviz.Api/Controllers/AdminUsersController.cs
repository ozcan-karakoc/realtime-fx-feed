using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

public sealed record UserView(int Id, string UserName, string Role, bool IsActive, DateTime CreatedUtc, DateTime? LastLoginUtc);
public sealed record CreateUserRequest([Required, MaxLength(64)] string UserName, [Required, MinLength(8)] string Password);
public sealed record ResetPasswordRequest([Required, MinLength(8)] string Password);

[ApiController]
[Route("api/v1/admin")]
[Authorize]
public sealed class AdminUsersController(AppDbContext db) : ControllerBase
{
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    [HttpGet("users")]
    public async Task<IEnumerable<UserView>> List(CancellationToken ct) =>
        await db.AdminUsers.AsNoTracking().OrderBy(u => u.UserName)
            .Select(u => new UserView(u.Id, u.UserName, u.Role, u.IsActive, u.CreatedUtc, u.LastLoginUtc))
            .ToListAsync(ct);

    [HttpPost("users")]
    public async Task<ActionResult<UserView>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var name = request.UserName.Trim();
        if (await db.AdminUsers.AnyAsync(u => u.UserName == name, ct))
            return Conflict(new ProblemDetails { Title = "Bu kullanıcı adı zaten var." });
        var user = new AdminUser { UserName = name };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.AdminUsers.Add(user);
        Audit("user.create", name);
        await db.SaveChangesAsync(ct);
        return new UserView(user.Id, user.UserName, user.Role, user.IsActive, user.CreatedUtc, null);
    }

    [HttpPut("users/{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.AdminUsers.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        Audit("user.password", user.UserName);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("users/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool value, CancellationToken ct)
    {
        var user = await db.AdminUsers.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (!value && user.UserName == User.Identity?.Name)
            return BadRequest(new ProblemDetails { Title = "Kendi hesabınızı pasif yapamazsınız." });
        if (!value && !await db.AdminUsers.AnyAsync(u => u.Id != id && u.IsActive, ct))
            return BadRequest(new ProblemDetails { Title = "Son aktif yönetici pasif yapılamaz." });
        user.IsActive = value;
        Audit(value ? "user.activate" : "user.deactivate", user.UserName);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Son işlem izi (giriş, marj kaydı, sağlayıcı değişimi, anahtar işlemleri).</summary>
    [HttpGet("audit")]
    public async Task<IEnumerable<AuditEntry>> AuditLog([FromQuery] int take = 100, CancellationToken ct = default) =>
        await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);

    private void Audit(string action, string detail) =>
        db.AuditEntries.Add(new AuditEntry { User = User.Identity?.Name ?? "", Action = action, Detail = detail });
}
