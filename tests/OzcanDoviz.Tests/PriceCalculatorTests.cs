using OzcanDoviz.Core.Prices;
using OzcanDoviz.Core.Pricing;

namespace OzcanDoviz.Tests;

public class PriceCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    private static ProductRule Rule(string code, string source, MarginMode mode, decimal buy, decimal sell, int decimals = 2) => new()
    {
        Code = code, Name = code, SourceSymbol = source, Decimals = decimals,
        Buy = new MarginSide { Mode = mode, Value = buy },
        Sell = new MarginSide { Mode = mode, Value = sell },
    };

    [Fact]
    public void Multiply_uses_has_buy_for_buy_and_has_sell_for_sell()
    {
        var quotes = new[] { new PriceQuote("HASALTIN", 6783.43m, 6829.52m, Now) };
        var item = Assert.Single(PriceCalculator.Apply([Rule("AYAR22", "HASALTIN", MarginMode.Multiply, 0.912m, 0.916m)], quotes));

        Assert.Equal(6186.49m, item.Buy);   // 6783.43 × 0.912 = 6186.488…
        Assert.Equal(6255.84m, item.Sell);  // 6829.52 × 0.916 = 6255.840…
    }

    [Fact]
    public void Add_and_percent_margins()
    {
        var quotes = new[] { new PriceQuote("USDTRY", 41.20m, 41.30m, Now) };
        var add = PriceCalculator.Apply([Rule("USD", "USDTRY", MarginMode.Add, -0.05m, 0.05m, 4)], quotes).Single();
        var pct = PriceCalculator.Apply([Rule("USD", "USDTRY", MarginMode.Percent, -1m, 1m, 4)], quotes).Single();

        Assert.Equal(41.15m, add.Buy);
        Assert.Equal(41.35m, add.Sell);
        Assert.Equal(40.788m, pct.Buy);
        Assert.Equal(41.713m, pct.Sell);
    }

    [Fact]
    public void Missing_source_hidden_and_non_positive_skipped()
    {
        var quotes = new[] { new PriceQuote("USDTRY", 1m, 1m, Now) };
        var rules = new[]
        {
            Rule("EUR", "EURTRY", MarginMode.Add, 0, 0),
            Rule("USD", "USDTRY", MarginMode.Add, -5, 0),
            new ProductRule { Code = "X", Name = "X", SourceSymbol = "USDTRY", Visible = false },
        };
        Assert.Empty(PriceCalculator.Apply(rules, quotes));
    }

    [Fact]
    public void Direction_follows_previous_sell()
    {
        var rule = Rule("USD", "USDTRY", MarginMode.Add, 0, 0, 4);
        var first = PriceCalculator.Apply([rule], [new PriceQuote("USDTRY", 41m, 41.1m, Now)]).Single();
        var prev = new Dictionary<string, BoardItem> { [first.Code] = first };

        var up = PriceCalculator.Apply([rule], [new PriceQuote("USDTRY", 41m, 41.2m, Now)], prev).Single();
        Assert.Equal("up", up.Direction);

        prev[up.Code] = up;
        var same = PriceCalculator.Apply([rule], [new PriceQuote("USDTRY", 41m, 41.2m, Now)], prev).Single();
        Assert.Equal("up", same.Direction);
    }

    [Fact]
    public void Validate_reports_duplicates_and_bad_coefficients()
    {
        var errors = PriceCalculator.Validate(
        [
            Rule("A", "HASALTIN", MarginMode.Multiply, 0, 1),
            Rule("a", "HASALTIN", MarginMode.Add, 0, 0),
            Rule("B", "", MarginMode.Percent, -100, 0),
        ]);

        Assert.Contains(errors, e => e.Contains("iki kez"));
        Assert.Contains(errors, e => e.Contains("katsayısı"));
        Assert.Contains(errors, e => e.Contains("kaynak sembol"));
        Assert.Contains(errors, e => e.Contains("yüzdesi"));
    }
}
