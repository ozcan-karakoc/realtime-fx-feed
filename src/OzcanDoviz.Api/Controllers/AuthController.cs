using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Controllers;

public sealed record LoginRequest([Required, MaxLength(64)] string UserName, [Required, MaxLength(128)] string Password);
public sealed record LoginResponse(string Token, DateTime ExpiresUtc, string UserName, string Role);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required, MinLength(8)] string NewPassword);

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AppDbContext db, TokenService tokens) : ControllerBase
{
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.UserName == request.UserName.Trim() && u.IsActive, ct);
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new ProblemDetails { Title = "Kullanıcı adı veya şifre hatalı." });

        user.LastLoginUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { User = user.UserName, Action = "login" });
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.Create(user);
        return new LoginResponse(token, expires, user.UserName, user.Role);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name, ct);
        if (user is null) return Unauthorized();
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new ProblemDetails { Title = "Mevcut şifre hatalı." });

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        db.AuditEntries.Add(new AuditEntry { User = user.UserName, Action = "password.change" });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
