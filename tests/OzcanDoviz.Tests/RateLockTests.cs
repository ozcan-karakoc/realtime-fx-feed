using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OzcanDoviz.Infrastructure.Locks;

namespace OzcanDoviz.Tests;

public sealed class LockLimitFactory : ApiFactory
{
    protected override int LockLimit => 2;
}

/// <summary>Mesaj botu kur sabitleme: fiyat sunucudan, ref no, JSON kaydı, panelden kapatma, ayarlar.</summary>
public sealed class RateLockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "admin1234" });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Request(string code = "USD", string side = "Buy", decimal quantity = 2, string phone = "0532 111 22 33") =>
        new { productCode = code, side, quantity, name = "Ayşe Yılmaz", phone };

    [Fact]
    public async Task Lock_uses_server_price_and_gets_reference()
    {
        factory.PublishPrices();
        var web = factory.CreateClient();

        var config = (await web.GetFromJsonAsync<JsonObject>("/api/v1/locks/config"))!;
        Assert.True(config["enabled"]!.GetValue<bool>());
        Assert.Equal(15, config["minutes"]!.GetValue<int>());

        // Müşteri alıyor → dükkanın satış fiyatı (41,30). İstemcinin gönderdiği fiyat yok sayılır.
        var r = await web.PostAsJsonAsync("/api/v1/locks", new { productCode = "usd", side = "Buy", quantity = 2, name = "Ayşe Yılmaz", phone = "0532 111 22 33", unitPrice = 1 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var created = (await r.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        var reference = created["ref"]!.GetValue<string>();
        Assert.Matches(new Regex("^OD-\\d{6}-[A-Z2-9]{5}$"), reference);
        Assert.Equal(41.30m, created["unitPrice"]!.GetValue<decimal>());
        Assert.Equal(82.60m, created["total"]!.GetValue<decimal>());
        Assert.Equal("Active", created["status"]!.GetValue<string>());
        Assert.Equal("A*** Y*****", created["customer"]!.GetValue<string>()); // ad maskeli, telefon hiç dönmez
        Assert.Null(created["customerPhone"]);
        var minutes = (created["expiresUtc"]!.GetValue<DateTime>() - created["createdUtc"]!.GetValue<DateTime>()).TotalMinutes;
        Assert.Equal(15, minutes, precision: 3);

        // Satış talebi alış fiyatından.
        var sell = (await (await web.PostAsJsonAsync("/api/v1/locks", Request(side: "Sell", quantity: 1))).Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.Equal(41.10m, sell["unitPrice"]!.GetValue<decimal>());

        // Referansla sorgu ve JSON dosyası.
        var found = (await web.GetFromJsonAsync<JsonObject>($"/api/v1/locks/{reference.ToLowerInvariant()}", Json))!;
        Assert.Equal(reference, found["ref"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/api/v1/locks/OD-000000-XXXXX")).StatusCode);
        var file = await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "locks.json"));
        Assert.Contains(reference, file);
        Assert.Contains("05321112233", file); // telefon rakamlara indirgenip panel için saklanır
    }

    [Theory]
    [InlineData("YOK", "Buy", 1, "05321112233", "Ürün")]
    [InlineData("USD", "Buy", 0, "05321112233", "Miktar")]
    [InlineData("USD", "Buy", 5000, "05321112233", "en fazla")]
    [InlineData("USD", "Buy", 1, "123", "Telefon")]
    public async Task Invalid_requests_are_rejected(string code, string side, decimal qty, string phone, string expected)
    {
        factory.PublishPrices();
        var r = await factory.CreateClient().PostAsJsonAsync("/api/v1/locks", Request(code, side, qty, phone));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(expected, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_closes_lock_and_changes_settings()
    {
        factory.PublishPrices();
        var web = factory.CreateClient();
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await web.GetAsync("/api/v1/admin/locks")).StatusCode);

        var reference = (await (await web.PostAsJsonAsync("/api/v1/locks", Request())).Content.ReadFromJsonAsync<JsonObject>())!["ref"]!.GetValue<string>();

        var list = (await admin.GetFromJsonAsync<JsonArray>("/api/v1/admin/locks"))!;
        var row = list.Single(x => x!["lock"]!["ref"]!.GetValue<string>() == reference)!;
        Assert.Equal("05321112233", row["lock"]!["customerPhone"]!.GetValue<string>());
        Assert.Equal("Active", row["status"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/admin/locks/{reference}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/admin/locks/{reference}/cancel", null)).StatusCode);
        var found = (await web.GetFromJsonAsync<JsonObject>($"/api/v1/locks/{reference}"))!;
        Assert.Equal("Completed", found["status"]!.GetValue<string>());

        // Süre 30 dk, sonra kapat: bot kabul etmez.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/locks/settings", new { enabled = true, minutes = 30, maxQuantity = 1000 })).StatusCode);
        Assert.Equal(30, (await web.GetFromJsonAsync<JsonObject>("/api/v1/locks/config"))!["minutes"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/locks/settings", new { enabled = true, minutes = 0, maxQuantity = 1 })).StatusCode);

        await admin.PutAsJsonAsync("/api/v1/admin/locks/settings", new { enabled = false, minutes = 30, maxQuantity = 1000 });
        var off = await web.PostAsJsonAsync("/api/v1/locks", Request());
        Assert.Equal(HttpStatusCode.BadRequest, off.StatusCode);
        Assert.Contains("kapalı", await off.Content.ReadAsStringAsync());
        await admin.PutAsJsonAsync("/api/v1/admin/locks/settings", new { enabled = true, minutes = 15, maxQuantity = 1000 });
    }

    [Fact]
    public void Reference_is_unique_and_unambiguous()
    {
        var existing = new HashSet<string>();
        for (var i = 0; i < 2000; i++)
        {
            var r = RateLockService.NewRef(new DateTime(2026, 10, 28), existing);
            Assert.StartsWith("OD-261028-", r);
            Assert.DoesNotMatch(new Regex("[01IO]", RegexOptions.None), r[10..]);
            Assert.True(existing.Add(r));
        }
    }
}

public sealed class RateLockLimitTests(LockLimitFactory factory) : IClassFixture<LockLimitFactory>
{
    [Fact]
    public async Task Too_many_locks_from_same_client_are_throttled()
    {
        factory.PublishPrices();
        var web = factory.CreateClient();
        var body = new { productCode = "USD", side = "Buy", quantity = 1, name = "Ali Veli", phone = "05321112233" };
        Assert.Equal(HttpStatusCode.OK, (await web.PostAsJsonAsync("/api/v1/locks", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await web.PostAsJsonAsync("/api/v1/locks", body)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await web.PostAsJsonAsync("/api/v1/locks", body)).StatusCode);
    }
}
