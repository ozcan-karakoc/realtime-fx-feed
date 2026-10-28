using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Displays;

namespace OzcanDoviz.Infrastructure.Storage;

public sealed class DisplayDocument
{
    public List<DisplayScreen> Screens { get; set; } = [];
}

/// <summary>TV / pano / LED ekran tanımları <c>App_Data/displays.json</c> dosyasındadır.</summary>
internal sealed class DisplayStore : IDisplayStore
{
    private readonly JsonFileStore<DisplayDocument> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DisplayStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<DisplayStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<DisplayDocument>(dir, "displays.json", options.Value.KeepHistory, () => new DisplayDocument(), logger);
    }

    public async Task<IReadOnlyList<DisplayScreen>> ListAsync(CancellationToken cancellationToken) =>
        (await _file.ReadAsync(cancellationToken)).Screens.OrderBy(s => s.Name).ToList();

    public async Task<DisplayScreen?> FindAsync(string id, CancellationToken cancellationToken) =>
        (await _file.ReadAsync(cancellationToken)).Screens.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<DisplayScreen> SaveAsync(DisplayScreen screen, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            screen.Id = screen.Id.Trim().ToLowerInvariant();
            screen.UpdatedUtc = DateTime.UtcNow;
            screen.Layout.ProductCodes = screen.Layout.ProductCodes
                .Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
            var screens = doc.Screens.Where(s => !s.Id.Equals(screen.Id, StringComparison.OrdinalIgnoreCase)).Append(screen).ToList();
            await _file.WriteAsync(new DisplayDocument { Screens = screens }, cancellationToken);
            return screen;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            var screens = doc.Screens.Where(s => !s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (screens.Count == doc.Screens.Count) return false;
            await _file.WriteAsync(new DisplayDocument { Screens = screens }, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
