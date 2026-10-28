using Microsoft.EntityFrameworkCore;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Auth;

/// <summary>Mobil/dış istemci anahtarını <c>X-Api-Key</c> başlığından veya <c>?key=</c> sorgusundan çözer.</summary>
public static class ApiClientResolver
{
    public static async Task<ApiClient?> FindAsync(AppDbContext db, HttpRequest request, CancellationToken ct)
    {
        var key = request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) key = request.Query["key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) return null;
        var hash = ApiKeys.Hash(key);
        return await db.ApiClients.FirstOrDefaultAsync(c => c.KeyHash == hash && c.IsActive, ct);
    }
}
