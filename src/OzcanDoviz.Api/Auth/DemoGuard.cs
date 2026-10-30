using OzcanDoviz.Core.Locks;
using OzcanDoviz.Infrastructure.Persistence;

namespace OzcanDoviz.Api.Auth;

/// <summary>
/// Demo hesabı paneli gezer ama hiçbir şey değiştiremez: panelde okuma dışındaki her istek reddedilir.
/// Sitenin herkese açık uçları (ör. botla kur sabitleme) etkilenmez.
/// </summary>
public static class DemoGuard
{
    /// <summary>Hiçbir şey kaydetmeyen POST uçları.</summary>
    private static readonly string[] SafePosts = ["/api/v1/admin/displays/preview"];

    public static bool Blocks(HttpContext ctx)
    {
        if (!ctx.User.IsInRole(AdminRoles.Demo)) return false;
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method) || HttpMethods.IsOptions(ctx.Request.Method)) return false;
        var path = ctx.Request.Path;
        if (SafePosts.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase))) return false;
        return path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/v1/auth/change-password", StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseDemoGuard(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (!Blocks(ctx)) { await next(); return; }
        await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Demo hesabında değişiklik kaydedilmez. Sistemi kendi hesabınızla kullanmak için bizimle iletişime geçin.").ExecuteAsync(ctx);
    });

    /// <summary>Demo hesabı müşterilerin ad ve telefonunu görmez.</summary>
    public static RateLock Mask(RateLock l) => new()
    {
        Ref = l.Ref, ProductCode = l.ProductCode, ProductName = l.ProductName, Side = l.Side,
        UnitPrice = l.UnitPrice, Quantity = l.Quantity, Total = l.Total, Decimals = l.Decimals,
        CustomerName = MaskName(l.CustomerName), CustomerPhone = MaskPhone(l.CustomerPhone), Note = null,
        Channel = l.Channel, CreatedUtc = l.CreatedUtc, ExpiresUtc = l.ExpiresUtc,
        ClosedStatus = l.ClosedStatus, ClosedUtc = l.ClosedUtc, ClosedBy = null,
    };

    private static string MaskName(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[..1] + "***"));

    private static string MaskPhone(string phone) =>
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 2) + phone[^2..];
}
