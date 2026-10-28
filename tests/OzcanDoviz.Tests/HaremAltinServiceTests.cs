using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OzcanDoviz.Core.Rates;
using OzcanDoviz.Infrastructure.Rates;

namespace OzcanDoviz.Tests;

public class HaremAltinServiceTests
{
    private const string Sample = """
        {"message":"","error":false,"data":[
          {"alis":"6783.4300","satis":"6829.5200","yuksek":"0","dusuk":"0","kayit_tarihi":"2026-09-22 23:59:01","kod":"ALTIN"},
          {"alis":"11057.0000","satis":"11164.0000","yuksek":"0","dusuk":"0","kayit_tarihi":"2026-09-22 23:59:01","kod":"CEYREK_YENI"},
          {"alis":"bozuk","satis":"1","yuksek":"0","dusuk":"0","kayit_tarihi":"2026-09-22 23:59:01","kod":"BOZUK"}
        ]}
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (HaremAltinService Service, StubHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.haremaltin.com/") };
        return (new HaremAltinService(client, NullLogger<HaremAltinService>.Instance), handler);
    }

    [Fact]
    public async Task Sends_dd_MM_yyyy_and_fixed_query()
    {
        var (service, handler) = Create(HttpStatusCode.OK, Sample);
        await service.GetHistoricalRatesAsync(new DateTime(2026, 9, 23));

        Assert.Equal("https://www.haremaltin.com/ajax/gecmis_kurlar?tip=1&tarih=23-09-2026&interval=gun", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task Parses_invariant_decimals_and_keeps_record_date()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // tr-TR'de "." binlik ayırıcıdır
        try
        {
            var (service, _) = Create(HttpStatusCode.OK, Sample);
            var rates = await service.GetHistoricalRatesAsync(new DateTime(2026, 9, 23));

            Assert.Equal(2, rates.Count); // bozuk satır atlanır
            var altin = rates.Single(r => r.Code == "ALTIN");
            Assert.Equal(6783.43m, altin.Buy);
            Assert.Equal(6829.52m, altin.Sell);
            Assert.Equal(new DateTime(2026, 9, 22, 23, 59, 1), altin.RecordDate); // istek tarihi değil
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Error_true_is_not_success()
    {
        var (service, _) = Create(HttpStatusCode.OK, """{"message":"tarih hatalı","error":true,"data":[]}""");
        var ex = await Assert.ThrowsAsync<HaremAltinException>(() => service.GetHistoricalRatesAsync(DateTime.Today));
        Assert.Contains("tarih hatalı", ex.Message);
    }

    [Fact]
    public async Task Non_200_throws_with_status()
    {
        var (service, _) = Create(HttpStatusCode.Forbidden, "<html>cf</html>");
        var ex = await Assert.ThrowsAsync<HaremAltinException>(() => service.GetHistoricalRatesAsync(DateTime.Today));
        Assert.Equal(403, ex.UpstreamStatus);
    }

    [Theory]
    [InlineData("6783.4300", true, 6783.43)]
    [InlineData("0", true, 0)]
    [InlineData("6.783,43", false, 0)]
    [InlineData("", false, 0)]
    public void TryParseDecimal_is_invariant(string text, bool ok, double expected)
    {
        Assert.Equal(ok, HaremAltinService.TryParseDecimal(text, out var value));
        if (ok) Assert.Equal((decimal)expected, value);
    }
}
