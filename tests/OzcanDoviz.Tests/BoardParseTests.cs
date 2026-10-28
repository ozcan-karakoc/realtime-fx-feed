using OzcanDoviz.Infrastructure.Providers;

namespace OzcanDoviz.Tests;

public class BoardParseTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Harem_socket_packet_adds_common_aliases()
    {
        const string packet = """42["price_changed",{"meta":{},"data":{"ALTIN":{"code":"ALTIN","alis":"6783.43","satis":6829.52},"CEYREK_YENI":{"code":"CEYREK_YENI","alis":"11057","satis":"11164"},"USDTRY":{"code":"USDTRY","alis":"41.20","satis":"41.30"}}}]""";
        var quotes = BoardParse.Harem(packet, Now);

        Assert.Contains(quotes, q => q.Symbol == "ALTIN" && q.Buy == 6783.43m);
        Assert.Contains(quotes, q => q.Symbol == "HASALTIN" && q.Sell == 6829.52m);
        Assert.Contains(quotes, q => q.Symbol == "CEYREK" && q.Buy == 11057m);
        Assert.Contains(quotes, q => q.Symbol == "USDTRY" && q.Sell == 41.30m);
    }

    [Fact]
    public void Ozbey_swaps_reversed_pairs_and_derives_has_from_kilo()
    {
        const string body = """{"KGRTRY":{"title":"KGRTRY","alis":"6830000","satis":"6780000"}}""";
        var quotes = BoardParse.Ozbey(body, Now);

        var kg = quotes.Single(q => q.Symbol == "KGRTRY");
        Assert.True(kg.Buy < kg.Sell);
        Assert.Contains(quotes, q => q.Symbol == "HASALTIN" && q.Buy == 6780m);
    }
}
