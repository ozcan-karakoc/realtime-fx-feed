using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OzcanDoviz.Core.Rates;

namespace OzcanDoviz.Infrastructure.Rates;

public sealed class HaremAltinOptions
{
    public const string Section = "HaremAltin";
    public string BaseUrl { get; set; } = "https://www.haremaltin.com";
}

/// <summary>Harem <c>/ajax/gecmis_kurlar</c> gövdesi. Sayılar metin olarak gelir.</summary>
internal sealed class HaremAltinApiResponse
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("error")] public bool Error { get; set; }
    [JsonPropertyName("data")] public List<HaremAltinApiRateDto>? Data { get; set; }
}

internal sealed class HaremAltinApiRateDto
{
    [JsonPropertyName("alis")] public string? Alis { get; set; }
    [JsonPropertyName("satis")] public string? Satis { get; set; }
    [JsonPropertyName("yuksek")] public string? Yuksek { get; set; }
    [JsonPropertyName("dusuk")] public string? Dusuk { get; set; }
    [JsonPropertyName("kayit_tarihi")] public string? KayitTarihi { get; set; }
    [JsonPropertyName("kod")] public string? Kod { get; set; }
}

/// <summary>
/// Harem Altın geçmiş kurları. Typed HttpClient (HttpClientFactory) ile kullanılır; BaseUrl ve sabit
/// başlıklar DI kaydında verilir. Cookie / cf_clearance kullanılmaz.
/// </summary>
internal sealed class HaremAltinService(HttpClient httpClient, ILogger<HaremAltinService> logger) : IHaremAltinService
{
    internal const string RequestDateFormat = "dd-MM-yyyy";
    internal const string RecordDateFormat = "yyyy-MM-dd HH:mm:ss";

    public Task<List<HaremAltinRateDto>> GetHistoricalRatesAsync(DateTime date, CancellationToken cancellationToken = default) =>
        GetHistoricalRatesAsync(date, HaremHistoryType.Gold, cancellationToken);

    public async Task<List<HaremAltinRateDto>> GetHistoricalRatesAsync(DateTime date, HaremHistoryType type, CancellationToken cancellationToken = default)
    {
        var tarih = date.ToString(RequestDateFormat, CultureInfo.InvariantCulture);
        var path = $"/ajax/gecmis_kurlar?tip={(int)type}&tarih={tarih}&interval=gun";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Harem geçmiş kur isteği gönderilemedi. Tarih: {Tarih}", tarih);
            throw new HaremAltinException("Harem Altın'a bağlanılamadı.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Harem geçmiş kur isteği zaman aşımına uğradı. Tarih: {Tarih}", tarih);
            throw new HaremAltinException("Harem Altın zamanında cevap vermedi.", inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                logger.LogWarning("Harem geçmiş kur {Status} döndü. Tarih: {Tarih}", status, tarih);
                var hint = status == 403 ? " (Cloudflare engeli olabilir)" : "";
                throw new HaremAltinException($"Harem Altın {status} döndü{hint}.", status);
            }

            HaremAltinApiResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<HaremAltinApiResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Harem geçmiş kur gövdesi JSON değil. Tarih: {Tarih}", tarih);
                throw new HaremAltinException("Harem Altın beklenmeyen bir cevap döndürdü.", inner: ex);
            }

            if (body is null)
                throw new HaremAltinException("Harem Altın boş cevap döndürdü.");
            if (body.Error)
            {
                logger.LogWarning("Harem geçmiş kur error=true: {Message}. Tarih: {Tarih}", body.Message, tarih);
                throw new HaremAltinException(string.IsNullOrWhiteSpace(body.Message)
                    ? "Harem Altın hata döndürdü."
                    : $"Harem Altın hata döndürdü: {body.Message}");
            }

            return Map(body.Data, logger);
        }
    }

    internal static List<HaremAltinRateDto> Map(IEnumerable<HaremAltinApiRateDto>? rows, ILogger? logger = null)
    {
        var list = new List<HaremAltinRateDto>();
        if (rows is null) return list;

        foreach (var row in rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Kod)) continue;
            if (!TryParseDecimal(row.Alis, out var buy) || !TryParseDecimal(row.Satis, out var sell))
            {
                logger?.LogWarning("Harem satırı atlandı, alış/satış sayı değil: {Kod}", row.Kod);
                continue;
            }
            // kayit_tarihi korunur; istek tarihi asla RecordDate'e yazılmaz.
            if (!DateTime.TryParseExact(row.KayitTarihi, RecordDateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var recordDate))
            {
                logger?.LogWarning("Harem satırı atlandı, kayit_tarihi okunamadı: {Kod} {Tarih}", row.Kod, row.KayitTarihi);
                continue;
            }

            list.Add(new HaremAltinRateDto
            {
                Code = row.Kod.Trim(),
                Buy = buy,
                Sell = sell,
                High = TryParseDecimal(row.Yuksek, out var high) ? high : 0m,
                Low = TryParseDecimal(row.Dusuk, out var low) ? low : 0m,
                RecordDate = recordDate,
            });
        }
        return list;
    }

    /// <summary>"6783.4300" → 6783.43. Nokta her zaman ondalık ayırıcıdır (InvariantCulture); binlik ayırıcı kabul edilmez.</summary>
    internal static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
}
