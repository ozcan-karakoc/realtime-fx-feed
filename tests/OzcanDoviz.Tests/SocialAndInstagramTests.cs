using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OzcanDoviz.Core.Content;

namespace OzcanDoviz.Tests;

/// <summary>Instagram Graph API taklidi. Geçerli anahtar: "IGAA" ile başlayan.</summary>
public sealed class FakeGraph : HttpMessageHandler
{
    public int MediaCalls { get; private set; }
    public List<string> Paths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Paths.Add(uri.AbsolutePath);
        var token = System.Web.HttpUtility.ParseQueryString(uri.Query)["access_token"] ?? "";
        if (!token.StartsWith("IGAA", StringComparison.Ordinal))
            // Gerçek API gibi hata gövdesinde anahtarı geri yansıtır; servis bunu dışarı vermemeli.
            return Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid OAuth access token " + token + "\"}}"));

        if (uri.AbsolutePath.EndsWith("/me", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, """{"username":"ozcandoviz","id":"1"}"""));
        if (uri.AbsolutePath.EndsWith("/me/media", StringComparison.Ordinal))
        {
            MediaCalls++;
            return Task.FromResult(Json(HttpStatusCode.OK, """
                {"data":[
                  {"id":"1","caption":"Yeni sezon","media_type":"IMAGE","media_url":"https://cdn.example/1.jpg","permalink":"https://www.instagram.com/p/1/","timestamp":"2026-10-27T10:00:00+0000"},
                  {"id":"2","media_type":"VIDEO","media_url":"https://cdn.example/2.mp4","thumbnail_url":"https://cdn.example/2.jpg","permalink":"https://www.instagram.com/p/2/","timestamp":"2026-10-26T10:00:00+0000"},
                  {"id":"3","media_type":"IMAGE","media_url":"javascript:alert(1)","permalink":"https://www.instagram.com/p/3/"}
                ]}
                """));
        }
        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public class SocialHrefTests
{
    [Theory]
    [InlineData("2026-10-27T10:00:00+0000", 10)]
    [InlineData("2026-10-27T13:00:00+0300", 10)]
    [InlineData("2026-10-27T10:00:00Z", 10)]
    public void Instagram_timestamps_parse(string value, int utcHour)
    {
        var t = OzcanDoviz.Infrastructure.Social.InstagramFeed.ParseTime(value);
        Assert.Equal(new DateTime(2026, 10, 27, utcHour, 0, 0), t);
    }

    private static string? Href(SocialType t, string v, string? msg = null) =>
        SiteContentRules.SocialHref(new SocialLink { Type = t, Value = v, Message = msg });

    [Fact]
    public void Builds_safe_links_from_user_values()
    {
        Assert.Equal("https://wa.me/905321112233", Href(SocialType.WhatsApp, "+90 532 111 22 33"));
        Assert.Equal("https://wa.me/905321112233?text=Fiyat%20alabilir%20miyim%3F", Href(SocialType.WhatsApp, "905321112233", "Fiyat alabilir miyim?"));
        Assert.Equal("https://www.instagram.com/ozcan.doviz/", Href(SocialType.Instagram, "@ozcan.doviz"));
        Assert.Equal("https://www.instagram.com/ozcan.doviz/", Href(SocialType.Instagram, "https://www.instagram.com/ozcan.doviz/?hl=tr"));
        Assert.Equal("tel:+902125550000", Href(SocialType.Phone, "+90 212 555 00 00"));
        Assert.Equal("https://t.me/ozcankanal", Href(SocialType.Telegram, "@ozcankanal"));
        Assert.Equal("https://www.facebook.com/ozcan", Href(SocialType.Facebook, "https://www.facebook.com/ozcan"));
    }

    [Theory]
    [InlineData(SocialType.WhatsApp, "123")]
    [InlineData(SocialType.Instagram, "javascript:alert(1)")]
    [InlineData(SocialType.Instagram, "https://evil.example/x")]
    [InlineData(SocialType.Instagram, "kötü isim")]
    [InlineData(SocialType.Facebook, "http://www.facebook.com/x")]
    [InlineData(SocialType.Facebook, "https://facebook.com.evil.example/x")]
    [InlineData(SocialType.YouTube, "javascript:alert(1)")]
    public void Rejects_unsafe_or_invalid(SocialType type, string value) => Assert.Null(Href(type, value));
}

public sealed class SocialAndInstagramTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    [Fact]
    public async Task Social_links_published_with_server_built_href()
    {
        var admin = await AdminAsync();
        var content = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/admin/content"))!;
        content["social"] = new JsonArray(
            new JsonObject { ["type"] = "WhatsApp", ["value"] = "0532 111 22 33 90", ["floating"] = true },
            new JsonObject { ["type"] = "Instagram", ["value"] = "@ozcandoviz", ["floating"] = true },
            new JsonObject { ["type"] = "Facebook", ["value"] = "https://www.facebook.com/ozcan", ["floating"] = false });
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).StatusCode);

        var pub = (await factory.CreateClient().GetFromJsonAsync<JsonObject>("/api/v1/content", Json))!;
        var social = pub["social"]!.AsArray();
        Assert.Equal(3, social.Count);
        Assert.Equal("https://www.instagram.com/ozcandoviz/", social[1]!["href"]!.GetValue<string>());
        Assert.False(social[2]!["floating"]!.GetValue<bool>());
        Assert.Equal("https://www.instagram.com/ozcandoviz/", pub["instagram"]!["profileUrl"]!.GetValue<string>());
        Assert.NotEmpty(pub["home"]!["features"]!.AsArray()); // demo tanıtım metinleri varsayılan
        var locations = pub["locations"]!.AsArray();
        Assert.Equal(2, locations.Count); // Kapalıçarşı ve Kuyumcukent demo şubeleri
        Assert.StartsWith("https://www.google.com/maps?q=", locations[0]!["embedUrl"]!.GetValue<string>());

        content["social"] = new JsonArray(new JsonObject { ["type"] = "Instagram", ["value"] = "javascript:alert(1)", ["floating"] = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/content", content)).StatusCode);
    }

    [Fact]
    public async Task Instagram_token_is_verified_kept_secret_and_feed_is_cached()
    {
        var admin = await AdminAsync();
        var web = factory.CreateClient();

        Assert.Empty((await web.GetFromJsonAsync<JsonArray>("/api/v1/content/instagram"))!); // anahtar yok
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/instagram/token", new { token = "GECERSIZ-ANAHTAR-0000000000" })).StatusCode);

        const string token = "IGAAtestTOKEN1234567890secret";
        var ok = await admin.PutAsJsonAsync("/api/v1/admin/instagram/token", new { token });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var status = (await ok.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("ozcandoviz", status["userName"]!.GetValue<string>());
        Assert.DoesNotContain(token, status.ToJsonString()); // yalnızca ipucu döner

        var posts = (await web.GetFromJsonAsync<JsonArray>("/api/v1/content/instagram", Json))!;
        Assert.Equal(2, posts.Count); // https olmayan görsel atlanır
        Assert.Equal("https://cdn.example/2.jpg", posts[1]!["imageUrl"]!.GetValue<string>()); // video → kapak görseli
        var calls = factory.Graph.MediaCalls;
        await web.GetFromJsonAsync<JsonArray>("/api/v1/content/instagram");
        Assert.Equal(calls, factory.Graph.MediaCalls); // 15 dk önbellek

        // Anahtar hiçbir herkese açık veya içerik cevabında ve content.json'da yok.
        Assert.DoesNotContain(token, await web.GetStringAsync("/api/v1/content"));
        Assert.DoesNotContain(token, await admin.GetStringAsync("/api/v1/admin/content"));
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "content.json")));
        Assert.Contains(token, await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "secrets.json")));

        Assert.Equal(HttpStatusCode.Unauthorized, (await web.GetAsync("/api/v1/admin/instagram")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/admin/instagram/token")).StatusCode);
        Assert.Empty((await web.GetFromJsonAsync<JsonArray>("/api/v1/content/instagram"))!);
    }
}
