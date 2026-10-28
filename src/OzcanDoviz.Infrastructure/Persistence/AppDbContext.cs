using Microsoft.EntityFrameworkCore;

namespace OzcanDoviz.Infrastructure.Persistence;

public sealed class AdminUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
}

/// <summary>
/// Mobil uygulama / dış site gibi WebSocket ve REST fiyat tüketicisi. Anahtarın yalnızca SHA-256 özeti saklanır;
/// düz anahtar oluşturulduğu anda bir kez gösterilir.
/// </summary>
public sealed class ApiClient
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string KeyHash { get; set; } = "";
    /// <summary>Virgülle ayrılmış ürün kodları; boşsa tüm görünür ürünler.</summary>
    public string? AllowedCodes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenUtc { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AdminUser>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32);
            e.HasIndex(x => x.UserName).IsUnique();
        });
        b.Entity<ApiClient>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.AllowedCodes).HasMaxLength(1000);
            e.HasIndex(x => x.KeyHash).IsUnique();
        });
        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.User).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => x.TimeUtc);
        });
    }
}
