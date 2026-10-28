namespace OzcanDoviz.Core.Locks;

/// <summary>Müşterinin niyeti. <see cref="Buy"/>: müşteri alıyor → dükkanın satış fiyatı. <see cref="Sell"/>: müşteri satıyor → alış fiyatı.</summary>
public enum LockSide { Buy, Sell }

public enum LockStatus { Active, Expired, Completed, Cancelled }

/// <summary>Mesaj botundan sabitlenen kur. <c>App_Data/locks.json</c> dosyasında durur.</summary>
public sealed class RateLock
{
    public string Ref { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public LockSide Side { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Total { get; set; }
    public int Decimals { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string? Note { get; set; }
    public string Channel { get; set; } = "web-bot";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    /// <summary>Yalnızca Completed veya Cancelled saklanır; Active/Expired süreden hesaplanır.</summary>
    public LockStatus? ClosedStatus { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string? ClosedBy { get; set; }

    public LockStatus StatusAt(DateTime nowUtc) =>
        ClosedStatus ?? (nowUtc < ExpiresUtc ? LockStatus.Active : LockStatus.Expired);
}

public sealed record RateLockRequest(string ProductCode, LockSide Side, decimal Quantity, string Name, string Phone, string? Note = null);

public interface IRateLockStore
{
    Task<RateLock> AddAsync(Func<ISet<string>, RateLock> create, CancellationToken cancellationToken);
    Task<RateLock?> FindAsync(string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<RateLock>> ListAsync(int take, CancellationToken cancellationToken);
    Task<RateLock?> CloseAsync(string reference, LockStatus status, string user, CancellationToken cancellationToken);
}

public interface IRateLockService
{
    Task<RateLock> CreateAsync(RateLockRequest request, CancellationToken cancellationToken);
}

/// <summary>Sabitleme yapılamadığında kullanıcıya gösterilecek açıklama.</summary>
public sealed class RateLockException(string message) : Exception(message);
