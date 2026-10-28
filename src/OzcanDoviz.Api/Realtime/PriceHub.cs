using Microsoft.AspNetCore.SignalR;
using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Api.Realtime;

/// <summary>Web sitesinin canlı fiyat hub'ı (<c>/hubs/prices</c>). Bağlanan istemci önce son tabloyu alır.</summary>
public sealed class PriceHub(IPriceBoard board) : Hub
{
    public const string Path = "/hubs/prices";
    public const string PricesEvent = "prices";
    /// <summary>Site içeriği değişti; istemci /api/v1/content'i yeniden çeker.</summary>
    public const string ContentEvent = "content";

    /// <summary>Kur sabitlemesinin durumu değişti (tamamlandı / iptal); yalnızca o referansı izleyenlere gider.</summary>
    public const string LockEvent = "lock";

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(PricesEvent, PublicPrices.From(board.Current), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Botta sabitleme yapan tarayıcı kendi referansını izler; panelde kapatılınca anında haber alır.
    /// Referansı bilen zaten /api/v1/locks/{ref} ile aynı (maskeli) bilgiyi görebildiği için ek yetki istenmez.
    /// </summary>
    public Task WatchLock(string reference)
    {
        var r = (reference ?? "").Trim().ToUpperInvariant();
        if (!LockGroups.IsValidRef(r)) throw new HubException("Geçersiz referans.");
        return Groups.AddToGroupAsync(Context.ConnectionId, LockGroups.Name(r), Context.ConnectionAborted);
    }
}

public static class LockGroups
{
    public static string Name(string reference) => "lock:" + reference.Trim().ToUpperInvariant();

    /// <summary>OD-YYMMDD-XXXXX</summary>
    public static bool IsValidRef(string r) =>
        r.Length == 15 && r.StartsWith("OD-", StringComparison.Ordinal) && r[9] == '-'
        && r.AsSpan(3, 6).IndexOfAnyExceptInRange('0', '9') < 0
        && r.AsSpan(10).IndexOfAnyExcept("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") < 0;
}

internal sealed class SignalRBroadcaster(IHubContext<PriceHub> hub) : IPriceBroadcaster
{
    public Task BroadcastAsync(BoardSnapshot snapshot, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(PriceHub.PricesEvent, PublicPrices.From(snapshot), cancellationToken);
}
