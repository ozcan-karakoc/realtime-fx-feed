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

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(PricesEvent, PublicPrices.From(board.Current), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRBroadcaster(IHubContext<PriceHub> hub) : IPriceBroadcaster
{
    public Task BroadcastAsync(BoardSnapshot snapshot, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(PriceHub.PricesEvent, PublicPrices.From(snapshot), cancellationToken);
}
