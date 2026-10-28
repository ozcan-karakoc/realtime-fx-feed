using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "OzcanDoviz";
    public string Audience { get; set; } = "OzcanDoviz.Admin";
    /// <summary>En az 32 karakter. Üretimde ortam değişkeni veya user-secrets ile verilmeli.</summary>
    public string Key { get; set; } = "";
    public int ExpiresHours { get; set; } = 12;

    public SymmetricSecurityKey SigningKey()
    {
        if (string.IsNullOrWhiteSpace(Key) || Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key en az 32 karakter olmalı.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
    }
}

public sealed class TokenService(IOptions<JwtOptions> options)
{
    public (string Token, DateTime ExpiresUtc) Create(AdminUser user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddHours(o.ExpiresHours);
        var token = new JwtSecurityToken(
            o.Issuer,
            o.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(o.SigningKey(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>Mobil/dış istemci anahtarı: <c>odz_</c> + 32 bayt rastgele. Veritabanında yalnızca SHA-256 özeti durur.</summary>
public static class ApiKeys
{
    public const string Prefix = "odz_";

    public static string Generate() =>
        Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant();

    public static string Display(string key) => key.Length > 12 ? key[..12] : key;
}
