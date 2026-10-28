using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Prices;
using OzcanDoviz.Infrastructure.Pricing;

namespace OzcanDoviz.Tests;

/// <summary>
/// Gerçek API'yi bellekte çalıştırır. Veriler geçici klasördedir; fiyat işçisi kapatılır (dış ağa çıkılmaz),
/// tabloya sabit bir fiyat yazılır.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "ozcan-it-" + Guid.NewGuid().ToString("N"));

    public FakeGraph Graph { get; } = new();

    /// <summary>Kur sabitleme istek sınırı; sınır testi dışında testler takılmasın diye yüksek.</summary>
    protected virtual int LockLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("RateLimits:LocksPer10Minutes", LockLimit.ToString());
        builder.UseSetting("Storage:DataDirectory", DataDir);
        builder.UseSetting("Media:Directory", Path.Combine(DataDir, "uploads"));
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(DataDir, "test.db")};Pooling=False");
        builder.ConfigureTestServices(services =>
        {
            // Instagram Graph API yerine sahte cevaplar: dış ağa çıkılmaz.
            services.AddHttpClient(nameof(OzcanDoviz.Infrastructure.Social.InstagramFeed))
                .ConfigurePrimaryHttpMessageHandler(() => Graph);
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PriceWorker));
            services.Remove(worker);
        });
    }

    public void PublishPrices()
    {
        var now = DateTime.UtcNow;
        var items = new List<BoardItem>
        {
            new("USD", "Amerikan Doları", "doviz", 41.10m, 41.30m, 4, "up", 41.10m, 41.20m, "USDTRY"),
            new("HAS", "24 Ayar Has Altın", "altin", 6480m, 6500m, 2, "same", 6480m, 6500m, "HASALTIN"),
        };
        Services.GetRequiredService<IPriceBoard>().Publish(
            new BoardSnapshot("test", "Test", now, items, null),
            new Dictionary<string, IReadOnlyList<PriceQuote>> { ["test"] = [new PriceQuote("USDTRY", 41.10m, 41.20m, now)] });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch (IOException) { }
    }
}

public sealed class ApiIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "admin1234" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonObject>(Json))!;

    [Theory]
    [InlineData("/api/v1/yok")]
    [InlineData("/media/yok.png")]
    public async Task Unknown_api_path_is_404_not_spa_html(string path)
    {
        var r = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("<html", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_endpoints_require_login()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/admin/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync("/api/v1/admin/content", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/admin/media")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/rates/harem/history?date=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task Ticker_saved_in_admin_appears_on_public_content_and_json_file()
    {
        var admin = await AdminAsync();
        var content = await ReadAsync(await admin.GetAsync("/api/v1/admin/content"));
        content["ticker"]!["items"] = new JsonArray(
            new JsonObject { ["text"] = "Kapalıçarşı şubemiz açıldı", ["enabled"] = true },
            new JsonObject { ["text"] = "Gizli duyuru", ["enabled"] = false },
            new JsonObject { ["text"] = "   ", ["enabled"] = true });
        content["ticker"]!["durationSeconds"] = 25;

        var save = await admin.PutAsJsonAsync("/api/v1/admin/content", content);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var pub = await ReadAsync(await factory.CreateClient().GetAsync("/api/v1/content"));
        var items = pub["ticker"]!["items"]!.AsArray().Select(i => i!.GetValue<string>()).ToList();
        Assert.Equal(["Kapalıçarşı şubemiz açıldı"], items); // pasif ve boş yazı yayınlanmaz
        Assert.Equal(25, pub["ticker"]!["durationSeconds"]!.GetValue<int>());
        Assert.Null(pub["updatedBy"]); // iç alan dışarı çıkmaz

        var file = await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "content.json"));
        Assert.Contains("Kapalıçarşı şubemiz açıldı", file); // Türkçe harf kaçırılmadan yazılır
    }

    [Fact]
    public async Task Invalid_content_is_rejected()
    {
        var admin = await AdminAsync();
        var content = await ReadAsync(await admin.GetAsync("/api/v1/admin/content"));
        content["brokerId"] = "Kötü Kimlik";
        content["contact"]!["mapEmbedUrl"] = "https://evil.example";

        var r = await admin.PutAsJsonAsync("/api/v1/admin/content", content);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("Aracı kimliği", body);
        Assert.Contains("Harita", body);
    }

    [Fact]
    public async Task Uploaded_photo_is_served_and_used_by_slide()
    {
        var admin = await AdminAsync();

        using var form = new MultipartFormDataContent();
        var png = new ByteArrayContent(MediaStorageTests.Png);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(png, "file", "vitrin.png");
        var upload = await admin.PostAsync("/api/v1/admin/media", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var url = (await ReadAsync(upload))["url"]!.GetValue<string>();

        var image = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());

        using var bad = new MultipartFormDataContent { { new StringContent("<html>"), "file", "foto.png" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/admin/media", bad)).StatusCode);

        var content = await ReadAsync(await admin.GetAsync("/api/v1/admin/content"));
        content["slides"] = new JsonArray(new JsonObject
        {
            ["id"] = "s1", ["imageUrl"] = url, ["title"] = "Yeni sezon", ["enabled"] = true, ["order"] = 1,
        });
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).StatusCode);

        // Kullanılan fotoğraf silinemez.
        var name = url["/media/".Length..];
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/admin/media/{name}")).StatusCode);
    }

    [Fact]
    public async Task Mobile_service_by_broker_id()
    {
        factory.PublishPrices();
        var admin = await AdminAsync();
        var created = await ReadAsync(await admin.PostAsJsonAsync("/api/v1/admin/clients", new { name = "iOS", allowedCodes = "USD" }));
        var key = created["apiKey"]!.GetValue<string>();
        var brokerId = (await ReadAsync(await admin.GetAsync("/api/v1/admin/content")))["brokerId"]!.GetValue<string>();

        var mobile = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync($"/api/v1/mobile/{brokerId}")).StatusCode);

        mobile.DefaultRequestHeaders.Add("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.NotFound, (await mobile.GetAsync("/api/v1/mobile/baska-araci")).StatusCode);

        var boot = await mobile.GetAsync($"/api/v1/mobile/{brokerId}");
        Assert.Equal(HttpStatusCode.OK, boot.StatusCode);
        var json = await ReadAsync(boot);
        Assert.Equal(brokerId, json["broker"]!["id"]!.GetValue<string>());
        var prices = json["prices"]!["items"]!.AsArray();
        Assert.Single(prices); // istemci filtresi: yalnızca USD
        Assert.Equal("USD", prices[0]!["code"]!.GetValue<string>());
        Assert.Null(prices[0]!["sourceSell"]); // ham fiyat mobile gitmez
        Assert.StartsWith("ws://", json["socketUrl"]!.GetValue<string>());
        Assert.NotNull(json["content"]!["ticker"]);

        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync($"/api/v1/mobile/{brokerId}/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync($"/api/v1/mobile/{brokerId}/content")).StatusCode);

        // Pasif istemci reddedilir.
        var id = created["client"]!["id"]!.GetValue<int>();
        await admin.PutAsJsonAsync($"/api/v1/admin/clients/{id}", new { name = "iOS", allowedCodes = "USD", isActive = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync($"/api/v1/mobile/{brokerId}/prices")).StatusCode);
    }

    [Fact]
    public async Task Mobile_content_uses_absolute_image_urls()
    {
        var admin = await AdminAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(MediaStorageTests.Png), "file", "a.png");
        var url = (await ReadAsync(await admin.PostAsync("/api/v1/admin/media", form)))["url"]!.GetValue<string>();

        var content = await ReadAsync(await admin.GetAsync("/api/v1/admin/content"));
        content["about"]!["imageUrl"] = url;
        (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).EnsureSuccessStatusCode();

        var key = (await ReadAsync(await admin.PostAsJsonAsync("/api/v1/admin/clients", new { name = "Android" })))["apiKey"]!.GetValue<string>();
        var mobile = factory.CreateClient();
        mobile.DefaultRequestHeaders.Add("X-Api-Key", key);
        var json = await ReadAsync(await mobile.GetAsync($"/api/v1/mobile/{content["brokerId"]!.GetValue<string>()}/content"));

        Assert.Equal("http://localhost" + url, json["about"]!["imageUrl"]!.GetValue<string>());
    }
}
