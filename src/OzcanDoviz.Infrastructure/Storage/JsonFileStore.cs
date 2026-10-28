using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OzcanDoviz.Infrastructure.Storage;

public sealed class StorageOptions
{
    /// <summary>JSON dosyalarının klasörü. Göreli yol uygulama klasörüne göre çözülür.</summary>
    public string DataDirectory { get; set; } = "App_Data";
    /// <summary>Her kayıtta önceki sürümün kopyası tutulur; en eski kopyalar bu sayıdan sonra silinir.</summary>
    public int KeepHistory { get; set; } = 50;
}

/// <summary>
/// Tek bir JSON dosyasını bellekte önbellekleyerek okur/yazar. Yazma atomiktir (geçici dosya → taşı),
/// önceki sürüm <c>history/</c> klasörüne kopyalanır. Bozuk dosya silinmez, <c>.bad</c> uzantısıyla saklanır.
/// </summary>
internal sealed class JsonFileStore<T> where T : class
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Dosya elle de okunur: Türkçe harfler ı gibi kaçırılmasın.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly string _historyDir;
    private readonly int _keepHistory;
    private readonly Func<T> _defaults;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private T? _cache;

    public JsonFileStore(string directory, string fileName, int keepHistory, Func<T> defaults, ILogger logger)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, fileName);
        _historyDir = Path.Combine(directory, "history");
        _keepHistory = keepHistory;
        _defaults = defaults;
        _logger = logger;
    }

    public string FilePath => _path;

    public async Task<T> ReadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        await _lock.WaitAsync(ct);
        try
        {
            return _cache ??= await LoadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> WriteAsync(T value, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (File.Exists(_path) && _keepHistory > 0)
            {
                Directory.CreateDirectory(_historyDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                var name = Path.GetFileNameWithoutExtension(_path);
                File.Copy(_path, Path.Combine(_historyDir, $"{name}-{stamp}.json"), overwrite: true);
                Prune(name);
            }

            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, value, Json, ct);
            File.Move(temp, _path, overwrite: true);
            _cache = value;
            return value;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<T> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            var seed = _defaults();
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, seed, Json, ct);
            _logger.LogInformation("{File} yoktu, varsayılan değerlerle oluşturuldu", _path);
            return seed;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct)
                   ?? throw new JsonException("Dosya boş.");
        }
        catch (JsonException ex)
        {
            var bad = $"{_path}.{DateTime.UtcNow:yyyyMMddHHmmss}.bad";
            File.Move(_path, bad);
            _logger.LogError(ex, "{File} okunamadı; {Bad} olarak saklandı, varsayılan değerler yüklendi", _path, bad);
            return await LoadAsync(ct);
        }
    }

    private void Prune(string name)
    {
        var old = new DirectoryInfo(_historyDir)
            .GetFiles(name + "-*.json")
            .OrderByDescending(f => f.Name)
            .Skip(_keepHistory);
        foreach (var file in old)
        {
            try { file.Delete(); }
            catch (IOException ex) { _logger.LogWarning(ex, "Eski kopya silinemedi: {File}", file.FullName); }
        }
    }
}
