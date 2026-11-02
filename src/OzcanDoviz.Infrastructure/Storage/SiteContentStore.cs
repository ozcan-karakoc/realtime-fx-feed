using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Content;

namespace OzcanDoviz.Infrastructure.Storage;

/// <summary>Site içeriği <c>App_Data/content.json</c> dosyasındadır.</summary>
internal sealed class SiteContentStore : ISiteContentStore
{
    private readonly JsonFileStore<SiteContent> _file;

    public SiteContentStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<SiteContentStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<SiteContent>(dir, "content.json", options.Value.KeepHistory, Defaults, logger);
    }

    public Task<SiteContent> GetAsync(CancellationToken cancellationToken) => _file.ReadAsync(cancellationToken);

    public Task<SiteContent> SaveAsync(SiteContent content, string? user, CancellationToken cancellationToken)
    {
        content.UpdatedUtc = DateTime.UtcNow;
        content.UpdatedBy = user;
        content.BrokerId = content.BrokerId.Trim().ToLowerInvariant();
        content.Ticker.Items = content.Ticker.Items
            .Select(i => new TickerItem { Text = (i.Text ?? "").Trim(), Enabled = i.Enabled })
            .Where(i => i.Text.Length > 0)
            .ToList();
        var order = 0;
        content.Slides = content.Slides.OrderBy(s => s.Order).ToList();
        foreach (var s in content.Slides)
        {
            s.Order = ++order;
            if (string.IsNullOrWhiteSpace(s.Id)) s.Id = Guid.NewGuid().ToString("N")[..8];
        }
        Normalize(content.Info);
        return _file.WriteAsync(content, cancellationToken);
    }

    /// <summary>Boş satırlar atılır; her satır sütun sayısı kadar hücre taşır.</summary>
    private static void Normalize(InfoSection info)
    {
        info.Intro = (info.Intro ?? "").Trim();
        info.DocumentsTitle = (info.DocumentsTitle ?? "").Trim();
        foreach (var t in info.Tables)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) t.Id = Guid.NewGuid().ToString("N")[..8];
            t.Title = (t.Title ?? "").Trim();
            t.Columns = t.Columns.Select(h => (h ?? "").Trim()).ToList();
            t.Rows = t.Rows
                .Select(r => new InfoRow
                {
                    Section = r.Section,
                    Cells = Enumerable.Range(0, t.Columns.Count).Select(i => i < r.Cells.Count ? (r.Cells[i] ?? "").Trim() : "").ToList(),
                })
                .Where(r => r.Cells.Any(x => x.Length > 0))
                .ToList();
        }
        foreach (var d in info.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) d.Id = Guid.NewGuid().ToString("N")[..8];
            d.Title = (d.Title ?? "").Trim();
        }
    }

    private static SiteContent Defaults() => new()
    {
        Ticker = new TickerSettings
        {
            Items =
            [
                new TickerItem { Text = "DEMO'ya hoş geldiniz." },
                new TickerItem { Text = "Fiyatlar bilgi amaçlıdır; işlem öncesi teyit ediniz." },
            ],
        },
        About = new AboutSection
        {
            Title = "Hakkımızda",
            Body = "DEMO olarak altın ve döviz alım satımında güvenilir hizmet sunuyoruz.\n\nBu metni yönetim panelindeki Site İçeriği sayfasından değiştirebilirsiniz.",
        },
        Contact = new ContactSection { WorkingHours = "Pazartesi - Cumartesi 09:00 - 19:00" },
    };
}

public sealed class MediaOptions
{
    /// <summary>Fotoğraf klasörü; göreli yol uygulama klasörüne göre çözülür.</summary>
    public string Directory { get; set; } = "App_Data/uploads";
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxDocumentBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed class MediaStorage : IMediaStorage
{
    public const string UrlPrefix = "/media/";

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ".jpg",
        [".jpeg"] = ".jpg",
        [".png"] = ".png",
        [".webp"] = ".webp",
    };

    public const string DocumentsUrlPrefix = UrlPrefix + "docs/";

    private readonly long _maxBytes;
    private readonly long _maxDocumentBytes;

    public MediaStorage(IOptions<MediaOptions> options, IHostEnvironment env)
    {
        Root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.Directory));
        _maxBytes = options.Value.MaxBytes;
        _maxDocumentBytes = options.Value.MaxDocumentBytes;
        System.IO.Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public async Task<MediaFile> SaveAsync(Stream content, string fileName, long length, CancellationToken cancellationToken)
    {
        if (length <= 0) throw new MediaValidationException("Dosya boş.");
        if (length > _maxBytes) throw new MediaValidationException($"Dosya en fazla {_maxBytes / 1024 / 1024} MB olabilir.");
        if (!Extensions.TryGetValue(Path.GetExtension(fileName ?? ""), out var ext))
            throw new MediaValidationException("Yalnızca JPG, PNG veya WebP yüklenebilir.");

        // Uzantıya güvenilmez: dosyanın ilk baytları gerçekten resim mi?
        var head = new byte[12];
        var read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var detected = Detect(head.AsSpan(0, read));
        if (detected is null || detected != ext)
            throw new MediaValidationException("Dosya içeriği uzantısıyla uyuşmuyor ya da resim değil.");

        var name = $"{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..26] + ext;
        var path = Path.Combine(Root, name);
        await using (var file = File.Create(path))
        {
            await file.WriteAsync(head.AsMemory(0, read), cancellationToken);
            await content.CopyToAsync(file, cancellationToken);
            if (file.Length > _maxBytes)
            {
                file.Close();
                File.Delete(path);
                throw new MediaValidationException($"Dosya en fazla {_maxBytes / 1024 / 1024} MB olabilir.");
            }
        }
        var info = new FileInfo(path);
        return new MediaFile(name, UrlPrefix + name, info.Length, info.CreationTimeUtc);
    }

    public IReadOnlyList<MediaFile> List() =>
        new DirectoryInfo(Root).GetFiles()
            .Where(f => Extensions.ContainsKey(f.Extension))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new MediaFile(f.Name, UrlPrefix + f.Name, f.Length, f.CreationTimeUtc))
            .ToList();

    public bool Delete(string name)
    {
        // Yalnızca klasördeki düz dosya adı; ../ ile dışarı çıkılamaz.
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return false;
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public async Task<MediaFile> SaveDocumentAsync(Stream content, string fileName, long length, CancellationToken cancellationToken)
    {
        var limit = $"Doküman en fazla {_maxDocumentBytes / 1024 / 1024} MB olabilir.";
        if (length <= 0) throw new MediaValidationException("Dosya boş.");
        if (length > _maxDocumentBytes) throw new MediaValidationException(limit);
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ext == ".jpeg") ext = ".jpg";

        // HTML veya betik içeren dosyalar kabul edilmez: uzantı beyaz listede olmalı ve imzası uymalı.
        var head = new byte[12];
        var read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (!DocumentMatches(ext, head.AsSpan(0, read)))
            throw new MediaValidationException("Yalnızca PDF, Word, Excel, PowerPoint, JPG veya PNG yüklenebilir; dosya içeriği uzantısıyla uyuşmalı.");

        var dir = Path.Combine(Root, "docs");
        System.IO.Directory.CreateDirectory(dir);
        var name = $"{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..26] + ext;
        var path = Path.Combine(dir, name);
        await using (var file = File.Create(path))
        {
            await file.WriteAsync(head.AsMemory(0, read), cancellationToken);
            await content.CopyToAsync(file, cancellationToken);
            if (file.Length > _maxDocumentBytes)
            {
                file.Close();
                File.Delete(path);
                throw new MediaValidationException(limit);
            }
        }
        var info = new FileInfo(path);
        return new MediaFile(name, DocumentsUrlPrefix + name, info.Length, info.CreationTimeUtc);
    }

    public bool DeleteDocument(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return false;
        var path = Path.Combine(Root, "docs", name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    internal static bool DocumentMatches(string ext, ReadOnlySpan<byte> b) => ext switch
    {
        ".pdf" => b.StartsWith("%PDF-"u8),
        ".doc" or ".xls" or ".ppt" => b.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
        ".docx" or ".xlsx" or ".pptx" => b.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        ".jpg" or ".png" => Detect(b) == ext,
        _ => false,
    };

    internal static string? Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
