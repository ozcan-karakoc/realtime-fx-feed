using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using OzcanDoviz.Core.Displays;
using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Tests;

public class DisplayFrameTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 14, 5, 0);

    private static readonly BoardItem[] Items =
    [
        new("USD", "Amerikan Doları", "doviz", 41.1m, 41.3m, 4, "up", 0, 0, "USDTRY"),
        new("CEYREK_YENI", "Yeni Çeyrek", "altin", 10581m, 10631m, 0, "down", 0, 0, "HASALTIN"),
        new("HAS", "24 Ayar Has Altın", "altin", 6482.44m, 6500.6m, 2, "same", 0, 0, "HASALTIN"),
    ];

    private static DisplayScreen Screen(Action<SerialSettings>? serial = null, params string[] codes)
    {
        var s = new DisplayScreen { Id = "led-1", Name = "LED", Kind = DisplayKind.Led, Output = DisplayOutput.Serial };
        s.Layout.ProductCodes = codes.ToList();
        serial?.Invoke(s.Serial);
        return s;
    }

    [Fact]
    public void Default_template_pads_columns_and_uses_crlf()
    {
        var text = DisplayFrame.BuildText(Screen(null, "USD", "HAS"), Items, Now);
        Assert.Equal(
            "USD     " + "   41.1000" + "   41.3000" + "\r\n" +
            "HAS     " + "   6482.44" + "   6500.60" + "\r\n", text);
    }

    [Fact]
    public void Turkish_numbers_folding_header_and_order()
    {
        var screen = Screen(s =>
        {
            s.NumberCulture = "tr";
            s.LineTemplate = "{name,-12}|{sell,8}|{dir}";
            s.HeaderTemplate = "OZCAN {time}";
            s.LineSeparator = "\\n";
        }, "CEYREK_YENI", "USD", "YOK");

        var text = DisplayFrame.BuildText(screen, Items, Now);
        Assert.Equal("OZCAN 14:05\nYENI CEYREK |  10.631|-\nAMERIKAN DOL| 41,3000|+\n", text);
    }

    [Fact]
    public void Prefix_suffix_and_turkish_code_page()
    {
        var screen = Screen(s =>
        {
            s.LineTemplate = "{name}";
            s.PrefixHex = "02";
            s.SuffixHex = "0x03 0D";
            s.AsciiFold = false;
            s.UpperCase = false;
            s.Encoding = "windows-1254";
            s.LineSeparator = "";
        }, "CEYREK_YENI");

        var bytes = DisplayFrame.Build(screen, Items, Now);
        Assert.Equal(0x02, bytes[0]);
        Assert.Equal([0x03, 0x0D], bytes[^2..]);
        Assert.Equal(0xC7, bytes[1 + 5]); // önek + "Yeni " → 'Ç' windows-1254'te tek bayt C7
        Assert.Equal("Yeni Çeyrek", Encoding.GetEncoding(1254).GetString(bytes[1..^2]));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("02", 1)]
    [InlineData("1B 40", 2)]
    [InlineData("0x02,0x03", 2)]
    [InlineData("ZZ", -1)]
    [InlineData("123", -1)]
    public void ParseHex(string input, int length)
    {
        var bytes = DisplayFrame.ParseHex(input);
        if (length < 0) Assert.Null(bytes);
        else Assert.Equal(length, bytes!.Length);
    }

    [Fact]
    public void Validation_catches_bad_settings()
    {
        var s = Screen(r => { r.BaudRate = 1234; r.PrefixHex = "XY"; });
        s.Id = "Kötü";
        s.Layout.ShowBuy = s.Layout.ShowSell = false;
        s.Output = DisplayOutput.Bluetooth;
        s.Bluetooth.ServiceUuid = "abc";
        var errors = DisplayRules.Validate(s);
        Assert.Contains(errors, e => e.Contains("kimliği"));
        Assert.Contains(errors, e => e.Contains("Baud"));
        Assert.Contains(errors, e => e.Contains("Başlangıç"));
        Assert.Contains(errors, e => e.Contains("Alış veya satış"));
        Assert.Contains(errors, e => e.Contains("UUID"));
        Assert.Empty(DisplayRules.Validate(Screen()));
    }
}

public sealed class DisplayApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
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
    public async Task Admin_creates_screen_public_reads_it_and_frame()
    {
        factory.PublishPrices();
        var web = factory.CreateClient();
        var admin = await AdminAsync();
        var screen = new
        {
            id = "led-vitrin", name = "Vitrin LED", kind = "Led", output = "Serial", enabled = true,
            layout = new { productCodes = new[] { "usd" }, showBuy = true, showSell = true, theme = "dark", orientation = "landscape", columns = 1, fontScale = 1, pageSeconds = 10 },
            serial = new { baudRate = 9600, dataBits = 8, parity = "none", stopBits = 1, encoding = "ascii", asciiFold = true, upperCase = true,
                           lineTemplate = "{code}={sell}", lineSeparator = "", prefixHex = "02", suffixHex = "03", numberCulture = "invariant", intervalSeconds = 5, headerTemplate = "" },
            bluetooth = new { serviceUuid = "0000ffe0-0000-1000-8000-00805f9b34fb", characteristicUuid = "0000ffe1-0000-1000-8000-00805f9b34fb", chunkSize = 20 },
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await web.PostAsJsonAsync("/api/v1/admin/displays", screen)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/admin/displays", screen)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/displays", screen)).StatusCode);

        var pub = (await web.GetFromJsonAsync<JsonObject>("/api/v1/displays/led-vitrin"))!;
        Assert.Equal("USD", pub["layout"]!["productCodes"]![0]!.GetValue<string>()); // kodlar büyük harfe

        var frame = await web.GetByteArrayAsync("/api/v1/displays/led-vitrin/frame");
        Assert.Equal("\u0002USD=41.3000\u0003", Encoding.ASCII.GetString(frame));
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "displays.json")));

        var preview = (await admin.PostAsJsonAsync("/api/v1/admin/displays/preview", screen));
        Assert.Equal("025553443D34312E3330303003", (await preview.Content.ReadFromJsonAsync<JsonObject>())!["hex"]!.GetValue<string>());

        // Kapalı ekran dışarıdan görünmez; geçersiz ayar kaydedilmez; silinir.
        var off = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(screen))!.AsObject();
        off["enabled"] = false;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/admin/displays/led-vitrin", off)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/api/v1/displays/led-vitrin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/api/v1/displays/led-vitrin/frame")).StatusCode);
        off["serial"]!["baudRate"] = 1234;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/displays/led-vitrin", off)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/admin/displays/led-vitrin")).StatusCode);
    }
}
