using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Locks;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Infrastructure.Locks;

public sealed class RateLockDocument
{
    public List<RateLock> Locks { get; set; } = [];
}

/// <summary>
/// Sabitlemeler <c>App_Data/locks.json</c> dosyasındadır. Her kayıt dosyanın tamamını yazar, bu yüzden geçmiş kopyası
/// tutulmaz; 180 günden eski kayıtlar ve 20.000'i aşanlar budanır.
/// </summary>
internal sealed class RateLockStore : IRateLockStore
{
    private const int MaxKeep = 20_000;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(180);

    private readonly JsonFileStore<RateLockDocument> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RateLockStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<RateLockStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        _file = new JsonFileStore<RateLockDocument>(dir, "locks.json", keepHistory: 0, () => new RateLockDocument(), logger);
    }

    public async Task<RateLock> AddAsync(Func<ISet<string>, RateLock> create, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            var refs = doc.Locks.Select(l => l.Ref).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var item = create(refs);
            var cutoff = DateTime.UtcNow - KeepFor;
            var locks = doc.Locks.Where(l => l.CreatedUtc >= cutoff).Append(item)
                .OrderByDescending(l => l.CreatedUtc).Take(MaxKeep).ToList();
            await _file.WriteAsync(new RateLockDocument { Locks = locks }, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RateLock?> FindAsync(string reference, CancellationToken cancellationToken)
    {
        var doc = await _file.ReadAsync(cancellationToken);
        return doc.Locks.FirstOrDefault(l => l.Ref.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<RateLock>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var doc = await _file.ReadAsync(cancellationToken);
        return doc.Locks.OrderByDescending(l => l.CreatedUtc).Take(take).ToList();
    }

    public async Task<RateLock?> CloseAsync(string reference, LockStatus status, string user, CancellationToken cancellationToken)
    {
        if (status is not (LockStatus.Completed or LockStatus.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(status));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var doc = await _file.ReadAsync(cancellationToken);
            var item = doc.Locks.FirstOrDefault(l => l.Ref.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase));
            if (item is null) return null;
            item.ClosedStatus = status;
            item.ClosedUtc = DateTime.UtcNow;
            item.ClosedBy = user;
            await _file.WriteAsync(doc, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Kur sabitleme. Fiyat istemciden alınmaz; o anki marjlı tablodan okunur. Süre ve azami miktar panel ayarındadır.
/// </summary>
internal sealed class RateLockService(IPriceBoard board, IFeedSettingsStore settings, IRateLockStore store) : IRateLockService
{
    private const string RefAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 0/O, 1/I karışmasın

    public async Task<RateLock> CreateAsync(RateLockRequest request, CancellationToken cancellationToken)
    {
        var options = (await settings.GetAsync(cancellationToken)).RateLock;
        if (!options.Enabled) throw new RateLockException("Kur sabitleme şu an kapalı.");

        var name = (request.Name ?? "").Trim();
        var phone = new string((request.Phone ?? "").Where(char.IsDigit).ToArray());
        if (name.Length is < 2 or > 80) throw new RateLockException("Ad soyad 2-80 karakter olmalı.");
        if (phone.Length is < 10 or > 13) throw new RateLockException("Telefon numarası geçersiz.");
        if (request.Quantity <= 0) throw new RateLockException("Miktar sıfırdan büyük olmalı.");
        if (request.Quantity > options.MaxQuantity)
            throw new RateLockException($"Tek seferde en fazla {options.MaxQuantity:0.##} birim sabitlenebilir.");

        var snapshot = board.Current;
        var item = snapshot.Items.FirstOrDefault(i => i.Code.Equals(request.ProductCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new RateLockException("Ürün bulunamadı veya şu an fiyatı yok.");
        if (snapshot.Error is not null && DateTime.UtcNow - snapshot.UpdatedUtc > TimeSpan.FromMinutes(2))
            throw new RateLockException("Fiyatlar şu an güncel değil; lütfen biraz sonra tekrar deneyin.");

        var unit = request.Side == LockSide.Buy ? item.Sell : item.Buy;
        var now = DateTime.UtcNow;
        return await store.AddAsync(existing => new RateLock
        {
            Ref = NewRef(now, existing),
            ProductCode = item.Code,
            ProductName = item.Name,
            Side = request.Side,
            UnitPrice = unit,
            Quantity = request.Quantity,
            Total = Math.Round(unit * request.Quantity, 2, MidpointRounding.AwayFromZero),
            Decimals = item.Decimals,
            CustomerName = name,
            CustomerPhone = phone,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()[..Math.Min(200, request.Note.Trim().Length)],
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(options.Minutes),
        }, cancellationToken);
    }

    internal static string NewRef(DateTime now, ISet<string> existing)
    {
        while (true)
        {
            var suffix = string.Create(5, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = RefAlphabet[RandomNumberGenerator.GetInt32(RefAlphabet.Length)];
            });
            var reference = $"OD-{now:yyMMdd}-{suffix}";
            if (!existing.Contains(reference)) return reference;
        }
    }
}
