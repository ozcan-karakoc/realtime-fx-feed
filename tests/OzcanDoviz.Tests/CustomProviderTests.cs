using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Infrastructure.Providers;

namespace OzcanDoviz.Tests;

public class CustomFeedParserTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void SocketIo_symbol_map_with_aliases()
    {
        var d = new CustomProviderDefinition
        {
            Url = "wss://x/socket.io/?EIO=4&transport=websocket", DataPath = "1.data", SymbolField = "code",
            BuyField = "alis", SellField = "satis", Aliases = "ALTIN=hasaltin\nbozuk satir",
        };
        const string msg = """42["price_changed",{"data":{"ALTIN":{"code":"ALTIN","alis":"6783.43","satis":6829.52},"USDTRY":{"alis":"41,10","satis":"41,20"}}}]""";

        var q = CustomFeedParser.Parse(msg, d, Now);
        Assert.Contains(q, x => x.Symbol == "ALTIN" && x.Buy == 6783.43m);
        Assert.Contains(q, x => x.Symbol == "HASALTIN" && x.Sell == 6829.52m); // eşleme
        Assert.Contains(q, x => x.Symbol == "USDTRY" && x.Buy == 41.10m);      // sembol alanı yoksa sözlük anahtarı
        Assert.Empty(CustomFeedParser.Parse("2", d, Now));                     // ping veri değil
        Assert.Empty(CustomFeedParser.Parse("""0{"sid":"x"}""", d, Now));
    }

    [Fact]
    public void Raw_array_with_nested_fields_and_names()
    {
        var d = new CustomProviderDefinition
        {
            Url = "wss://x/prices", Protocol = "raw", DataPath = "result.items", SymbolField = "s",
            BuyField = "quote.bid", SellField = "quote.ask", NameField = "n",
        };
        const string msg = """{"result":{"items":[{"s":"eurtry","n":"Euro","quote":{"bid":55.3,"ask":"55.45"}},{"s":"BAD","quote":{"bid":0,"ask":1}}]}}""";

        var q = Assert.Single(CustomFeedParser.Parse(msg, d, Now));
        Assert.Equal("EURTRY", q.Symbol);
        Assert.Equal("Euro", q.Name);
        Assert.Equal(55.45m, q.Sell);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"other":[]}""")]
    [InlineData("""{"data":"text"}""")]
    public void Bad_or_unmapped_messages_give_nothing(string msg)
    {
        var d = new CustomProviderDefinition { Url = "wss://x", Protocol = "raw", DataPath = "data" };
        Assert.Empty(CustomFeedParser.Parse(msg, d, Now));
    }

    [Theory]
    [InlineData("ftp://x/feed", "Adres")]
    [InlineData("javascript:alert(1)", "Adres")]
    [InlineData("wss://user:pass@x/feed", "kullanıcı")]
    public void Rejects_bad_urls(string url, string expected)
    {
        var d = new CustomProviderDefinition { Key = "ozel", Name = "Özel", Url = url };
        Assert.Contains(CustomProviderRules.Validate(d, []), e => e.Contains(expected));
    }

    [Fact]
    public void Key_cannot_collide_with_built_in()
    {
        var d = new CustomProviderDefinition { Key = "harem", Name = "Sahte", Url = "wss://x/feed" };
        Assert.Contains(CustomProviderRules.Validate(d, ["harem"]), e => e.Contains("hazır"));
    }
}

/// <summary>Test içinde gerçek bir WebSocket sunucusu: /sio socket.io gibi, /raw düz JSON gönderir.</summary>
public sealed class FakeFeedServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string BaseWs { get; }

    private FakeFeedServer(WebApplication app, string baseWs) { _app = app; BaseWs = baseWs; }

    public static async Task<FakeFeedServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();

        app.Map("/sio", async (HttpContext ctx) =>
        {
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await Send(ws, """0{"sid":"abc","pingInterval":25000}""");
            var buf = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                if (text == "40")
                {
                    await Send(ws, """40{"sid":"ns"}""");
                    await Send(ws, "2"); // ping
                }
                else if (text.StartsWith("42[\"subscribe\"", StringComparison.Ordinal))
                    await Send(ws, """42["price_changed",{"data":{"USDTRY":{"code":"USDTRY","alis":"41.10","satis":"41.20"},"ALTIN":{"code":"ALTIN","alis":"6480","satis":"6500"}}}]""");
            }
        });

        app.Map("/raw", async (HttpContext ctx) =>
        {
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await Send(ws, """{"type":"hello"}""");
            await Send(ws, """{"data":[{"symbol":"EURTRY","bid":55.3,"ask":55.4}]}""");
            try { await Task.Delay(Timeout.Infinite, ctx.RequestAborted); } catch (OperationCanceledException) { }
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new FakeFeedServer(app, address.Replace("http://", "ws://"));
    }

    private static Task Send(WebSocket ws, string text) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed class CustomProviderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "admin1234" });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task SocketIo_feed_test_save_use_and_delete()
    {
        await using var feed = await FakeFeedServer.StartAsync();
        var admin = await AdminAsync();
        var def = new
        {
            key = "ozel-sio", name = "Özel socket.io", url = feed.BaseWs + "/sio?EIO=4&transport=websocket", enabled = true,
            protocol = "socketio", subscribeMessage = """42["subscribe","prices"]""",
            dataPath = "1.data", symbolField = "code", buyField = "alis", sellField = "satis", aliases = "ALTIN=HASALTIN",
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/v1/admin/custom-providers/test", def)).StatusCode);

        // Dene: el sıkışma, ping/pong, abonelik → fiyat.
        var test = (await (await admin.PostAsJsonAsync("/api/v1/admin/custom-providers/test", def)).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(test["ok"]!.GetValue<bool>(), test["message"]!.GetValue<string>());
        var symbols = test["symbols"]!.AsArray().Select(s => s!.GetValue<string>()).ToList();
        Assert.Contains("USDTRY", symbols);
        Assert.Contains("HASALTIN", symbols);

        // Kaydet: hazır sağlayıcılarla birlikte listede, dosyada ve çalışır durumda.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/custom-providers/ozel-sio", def)).StatusCode);
        var providers = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/admin/providers"))!["providers"]!.AsArray();
        Assert.Contains(providers, p => p!["key"]!.GetValue<string>() == "ozel-sio");
        Assert.Contains("ozel-sio", await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "providers.json")));

        var live = factory.Services.GetRequiredService<IProviderRegistry>().Find("ozel-sio")!;
        var quotes = await live.GetQuotesAsync(CancellationToken.None);
        Assert.Contains(quotes, q => q.Symbol == "USDTRY" && q.Sell == 41.20m);

        // Kategoride kullanılırken silinemez.
        var settings = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/admin/providers"))!["settings"]!.AsObject();
        settings["categoryProviders"] = new JsonObject { ["doviz"] = "ozel-sio" };
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/providers", settings)).StatusCode);
        var conflict = await admin.DeleteAsync("/api/v1/admin/custom-providers/ozel-sio");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("doviz", await conflict.Content.ReadAsStringAsync());

        settings["categoryProviders"] = new JsonObject();
        await admin.PutAsJsonAsync("/api/v1/admin/providers", settings);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/admin/custom-providers/ozel-sio")).StatusCode);
        Assert.Null(factory.Services.GetRequiredService<IProviderRegistry>().Find("ozel-sio"));
    }

    [Fact]
    public async Task Raw_feed_and_helpful_failures()
    {
        await using var feed = await FakeFeedServer.StartAsync();
        var admin = await AdminAsync();
        object Def(string url, string path) => new
        {
            key = "ozel-raw", name = "Özel raw", url, enabled = true, protocol = "raw",
            dataPath = path, symbolField = "symbol", buyField = "bid", sellField = "ask",
        };

        var ok = (await (await admin.PostAsJsonAsync("/api/v1/admin/custom-providers/test", Def(feed.BaseWs + "/raw", "data"))).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(ok["ok"]!.GetValue<bool>());
        Assert.Equal("EURTRY", ok["symbols"]![0]!.GetValue<string>());

        // Yanlış yol: mesaj gelir ama eşlenemez; örnek mesaj gösterilir.
        var wrong = (await (await admin.PostAsJsonAsync("/api/v1/admin/custom-providers/test", Def(feed.BaseWs + "/raw", "items"))).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(wrong["ok"]!.GetValue<bool>());
        Assert.Contains("Veri yolu", wrong["message"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(wrong["sample"]?.GetValue<string>()));

        // Ulaşılamayan adres.
        var down = (await (await admin.PostAsJsonAsync("/api/v1/admin/custom-providers/test", Def("ws://127.0.0.1:1/x", "data"))).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(down["ok"]!.GetValue<bool>());

        // Hazır anahtar ve geçersiz şema kaydedilmez.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/custom-providers/harem",
            new { key = "harem", name = "x", url = "wss://x/y", buyField = "a", sellField = "b", protocol = "auto" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/custom-providers/test", Def("file:///etc/passwd", "data"))).StatusCode);
    }
}
