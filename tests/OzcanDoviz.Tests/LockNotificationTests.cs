using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OzcanDoviz.Api.Realtime;

namespace OzcanDoviz.Tests;

/// <summary>Panelde "Tamamlandı" / "İptal" basılınca müşterinin sayfasına SignalR ile anında haber gider.</summary>
public sealed class LockNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/prices"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "admin1234" });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> CreateLockAsync()
    {
        var r = await factory.CreateClient().PostAsJsonAsync("/api/v1/locks",
            new { productCode = "USD", side = "Buy", quantity = 1, name = "Ali Veli", phone = "05321112233" });
        return (await r.Content.ReadFromJsonAsync<JsonObject>())!["ref"]!.GetValue<string>();
    }

    [Fact]
    public async Task Completing_a_lock_notifies_only_its_watchers_instantly()
    {
        factory.PublishPrices();
        var mine = await CreateLockAsync();
        var other = await CreateLockAsync();

        await using var customer = Connect();
        await using var stranger = Connect();
        var received = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var strangerGot = false;
        customer.On<JsonObject>(PriceHub.LockEvent, u => received.TrySetResult(u));
        stranger.On<JsonObject>(PriceHub.LockEvent, _ => strangerGot = true);
        await customer.StartAsync();
        await stranger.StartAsync();
        await customer.InvokeAsync("WatchLock", mine.ToLowerInvariant()); // küçük harf de kabul
        await stranger.InvokeAsync("WatchLock", other);

        var admin = await AdminAsync();
        (await admin.PostAsync($"/api/v1/admin/locks/{mine}/complete", null)).EnsureSuccessStatusCode();

        var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(mine, update["ref"]!.GetValue<string>());
        Assert.Equal("Completed", update["status"]!.GetValue<string>());
        Assert.Equal("A** V***", update["customer"]!.GetValue<string>()); // maskeli; telefon yok
        Assert.Null(update["customerPhone"]);

        await Task.Delay(500);
        Assert.False(strangerGot); // başka referansı izleyen haber almaz
    }

    [Fact]
    public async Task Cancel_is_pushed_and_bad_refs_are_rejected()
    {
        factory.PublishPrices();
        var reference = await CreateLockAsync();
        await using var customer = Connect();
        var received = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        customer.On<JsonObject>(PriceHub.LockEvent, u => received.TrySetResult(u));
        await customer.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => customer.InvokeAsync("WatchLock", "herkes"));
        await customer.InvokeAsync("WatchLock", reference);

        var admin = await AdminAsync();
        (await admin.PostAsync($"/api/v1/admin/locks/{reference}/cancel", null)).EnsureSuccessStatusCode();
        var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Cancelled", update["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("OD-261028-69ZPA", true)]
    [InlineData("OD-261028-69ZP0", false)]
    [InlineData("OD-2610X8-69ZPA", false)]
    [InlineData("lock:*", false)]
    public void Reference_format(string r, bool ok) => Assert.Equal(ok, LockGroups.IsValidRef(r));
}
