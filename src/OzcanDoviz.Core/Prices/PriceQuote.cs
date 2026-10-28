namespace OzcanDoviz.Core.Prices;

/// <summary>Sağlayıcıdan gelen ham alış/satış. Marj uygulanmamıştır.</summary>
public sealed record PriceQuote(string Symbol, decimal Buy, decimal Sell, DateTime TimeUtc, string? Name = null);
