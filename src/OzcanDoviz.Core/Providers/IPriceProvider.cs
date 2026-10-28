using OzcanDoviz.Core.Prices;

namespace OzcanDoviz.Core.Providers;

public enum ProviderTransport { Socket, Http }

/// <summary>
/// Fiyat sağlayıcı sözleşmesi. Her sağlayıcı ham kodun yanına ortak kodu da yazar
/// (HASALTIN, GRAMALTIN, CEYREK, USDTRY…); marj kuralları ortak koda bağlanır, sağlayıcı değişince kural bozulmaz.
/// </summary>
public interface IPriceProvider
{
    string Key { get; }
    string DisplayName { get; }
    string Description { get; }
    ProviderTransport Transport { get; }

    Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken cancellationToken);
}

public sealed record ProviderInfo(string Key, string DisplayName, string Description, string Transport);

public sealed record ProviderTestResult(bool Ok, string Message, int SymbolCount = 0);

public interface IProviderRegistry
{
    IReadOnlyList<IPriceProvider> All { get; }
    IPriceProvider? Find(string? key);
}
