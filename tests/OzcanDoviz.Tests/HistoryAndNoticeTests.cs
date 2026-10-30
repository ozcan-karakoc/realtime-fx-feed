using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Content;

namespace OzcanDoviz.Tests;

/// <summary>
/// Harem geçmiş kur taklidi: istenen günün bir önceki gününün kaydını döndürür (Harem gibi);
/// cumartesi ve pazar isteği cumanın kaydını döndürür. Fiyat = 6000 + gün sırası.
/// </summary>
public sealed class FakeHaremHistory : HttpMessageHandler
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var asked = DateTime.ParseExact(q["tarih"]!, "dd-MM-yyyy", CultureInfo.InvariantCulture);
        var record = asked.AddDays(-1);
        while (record.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) record = record.AddDays(-1);
        var price = 6000 + record.DayOfYear;
        var code = q["tip"] == "0" ? "USDTRY" : "ALTIN";
        var body = $$"""{"message":"","error":false,"data":[{"alis":"{{price}}.10","satis":"{{price + 20}}.50","yuksek":"0","dusuk":"0","kayit_tarihi":"{{record:yyyy-MM-dd}} 23:59:01","kod":"{{code}}"}]}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public sealed class HistoryAndNoticeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "admin1234" });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/prices"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

    [Fact]
    public async Task History_series_dedupes_weekends_caches_days_and_respects_menu_toggle()
    {
        var web = factory.CreateClient();
        var codes = (await web.GetFromJsonAsync<JsonObject>("/api/v1/history/codes"))!;
        Assert.Contains(codes["codes"]!.AsArray(), c => c!["code"]!.GetValue<string>() == "USDTRY");

        var series = (await web.GetFromJsonAsync<JsonObject>("/api/v1/history/series?code=ALTIN&days=30"))!;
        var points = series["points"]!.AsArray();
        var dates = points.Select(p => DateOnly.Parse(p!["date"]!.GetValue<string>(), CultureInfo.InvariantCulture)).ToList();
        Assert.True(points.Count is >= 18 and <= 24, $"{points.Count} iş günü"); // 30 istekten hafta sonu tekrarları ayıklanır
        Assert.Equal(dates.Count, dates.Distinct().Count());
        Assert.All(dates, d => Assert.True(d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)));
        Assert.Equal(dates.OrderBy(d => d), dates);
        var first = points[0]!;
        Assert.Equal(6000 + dates[0].DayOfYear + 0.10m, first["buy"]!.GetValue<decimal>());

        // Geçmiş günler önbellekten: aynı seri Harem'e yeniden gitmez (yalnızca "bugün" tazelenebilir).
        var before = factory.Harem.Calls;
        await web.GetFromJsonAsync<JsonObject>("/api/v1/history/series?code=ALTIN&days=7");
        Assert.True(factory.Harem.Calls - before <= 1);
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "history-cache.json")));

        // Döviz ayrı tipten (tip=0) gelir.
        var usd = (await web.GetFromJsonAsync<JsonObject>("/api/v1/history/series?code=USDTRY&days=7"))!;
        Assert.NotEmpty(usd["points"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/api/v1/history/series?code=YOK&days=7")).StatusCode);

        // Panelde sayfa kapatılınca uçlar 404, menü bilgisi içerikte.
        var admin = await AdminAsync();
        var content = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/admin/content"))!;
        content["pages"]!["history"] = false;
        (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/api/v1/history/series?code=ALTIN&days=7")).StatusCode);
        Assert.False((await web.GetFromJsonAsync<JsonObject>("/api/v1/content"))!["pages"]!["history"]!.GetValue<bool>());
        content["pages"]!["history"] = true;
        (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Notice_is_pushed_instantly_and_can_be_withdrawn()
    {
        await using var visitor = Connect();
        var got = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        visitor.On<JsonObject>(PriceHub.NoticeEvent, n => got.TrySetResult(n));
        visitor.On<string>(PriceHub.NoticeRemovedEvent, id => removed.TrySetResult(id));
        await visitor.StartAsync();

        var web = factory.CreateClient();
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await web.PostAsJsonAsync("/api/v1/admin/notices", new { title = "x", level = "info" })).StatusCode);

        var list = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/admin/notices"))!;
        Assert.True(list["online"]!.GetValue<int>() >= 1); // bu test istemcisi açık

        var sent = await admin.PostAsJsonAsync("/api/v1/admin/notices",
            new { title = "Bugün 19:00'a kadar açığız", message = "Kapalıçarşı şubesi", level = "success", linkUrl = "/iletisim", linkText = "Adres", displayMinutes = 60 });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var id = (await sent.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        var notice = await got.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Bugün 19:00'a kadar açığız", notice["title"]!.GetValue<string>());
        Assert.Null(notice["createdBy"]); // kim gönderdi siteye gitmez

        // Süreli bildirimi siteyi sonradan açan da görür.
        var active = (await web.GetFromJsonAsync<JsonArray>("/api/v1/notices/active"))!;
        Assert.Contains(active, n => n!["id"]!.GetValue<string>() == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/notices/{id}")).StatusCode);
        Assert.Equal(id, await removed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain((await web.GetFromJsonAsync<JsonArray>("/api/v1/notices/active"))!, n => n!["id"]!.GetValue<string>() == id);
    }

    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//evil.example", false)]
    [InlineData("http://x.example", false)]
    [InlineData("/canli-piyasalar", true)]
    [InlineData("https://ornek.com/kampanya", true)]
    public void Notice_links_are_safe(string url, bool ok) => Assert.Equal(ok, SiteNoticeRules.IsSafeLink(url));

    [Fact]
    public async Task Instant_only_notice_is_not_shown_to_later_visitors()
    {
        var admin = await AdminAsync();
        var sent = await admin.PostAsJsonAsync("/api/v1/admin/notices", new { title = "Anlık", level = "info", displayMinutes = 0 });
        var id = (await sent.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var active = (await factory.CreateClient().GetFromJsonAsync<JsonArray>("/api/v1/notices/active"))!;
        Assert.DoesNotContain(active, n => n!["id"]!.GetValue<string>() == id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/notices", new { title = "", level = "info" })).StatusCode);
    }
}
