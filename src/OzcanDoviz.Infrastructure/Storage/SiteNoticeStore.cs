using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Content;

namespace OzcanDoviz.Infrastructure.Storage;

public sealed class SiteNoticeDocument
{
    public List<SiteNotice> Notices { get; set; } = [];
}

/// <summary>Gönderilen bildirimler <c>App_Data/notices.json</c> dosyasındadır (son 200).</summary>
internal sealed class SiteNoticeStore : ISiteNoticeStore
{
    private const int Keep = 200;
    private readonly JsonFileStore<SiteNoticeDocument> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SiteNoticeStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<SiteNoticeStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<SiteNoticeDocument>(dir, "notices.json", keepHistory: 0, () => new SiteNoticeDocument(), logger);
    }

    public async Task<IReadOnlyList<SiteNotice>> ListAsync(int take, CancellationToken cancellationToken) =>
        (await _file.ReadAsync(cancellationToken)).Notices.OrderByDescending(n => n.CreatedUtc).Take(take).ToList();

    public async Task<SiteNotice> AddAsync(SiteNotice notice, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            var list = doc.Notices.Append(notice).OrderByDescending(n => n.CreatedUtc).Take(Keep).ToList();
            await _file.WriteAsync(new SiteNoticeDocument { Notices = list }, cancellationToken);
            return notice;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SiteNotice?> WithdrawAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            var item = doc.Notices.FirstOrDefault(n => n.Id == id);
            if (item is null || item.WithdrawnUtc is not null) return item;
            item.WithdrawnUtc = DateTime.UtcNow;
            await _file.WriteAsync(doc, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }
}
