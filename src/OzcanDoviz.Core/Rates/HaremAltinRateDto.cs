namespace OzcanDoviz.Core.Rates;

/// <summary>
/// Harem Altın geçmiş kur satırı. <see cref="RecordDate"/> Harem'in döndürdüğü kayit_tarihi'dir;
/// istek tarihi değildir (23-09 isteğine 22-09 23:59:01 kaydı gelebilir).
/// </summary>
public sealed class HaremAltinRateDto
{
    public string Code { get; set; } = "";
    public decimal Buy { get; set; }
    public decimal Sell { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public DateTime RecordDate { get; set; }
}

public interface IHaremAltinService
{
    Task<List<HaremAltinRateDto>> GetHistoricalRatesAsync(DateTime date, CancellationToken cancellationToken = default);
}

/// <summary>Harem geçmiş kur ucu başarısız döndüğünde (HTTP hatası, error=true, bozuk gövde).</summary>
public sealed class HaremAltinException(string message, int? upstreamStatus = null, Exception? inner = null)
    : Exception(message, inner)
{
    public int? UpstreamStatus { get; } = upstreamStatus;
}
