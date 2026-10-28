using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OzcanDoviz.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Şemayı oluşturur ve hiç yönetici yoksa <c>Admin:UserName</c> / <c>Admin:Password</c> ile ilk yöneticiyi yazar.
    /// Şema basit tutulduğu için EnsureCreated kullanılır; şema değişirse migration'a geçilmelidir.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        await db.Database.EnsureCreatedAsync(ct);

        if (await db.AdminUsers.AnyAsync(ct)) return;

        var userName = config["Admin:UserName"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Hiç yönetici yok ve Admin:UserName / Admin:Password tanımlı değil; panele giriş yapılamaz.");
            return;
        }

        var user = new AdminUser { UserName = userName.Trim() };
        user.PasswordHash = new PasswordHasher<AdminUser>().HashPassword(user, password);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("İlk yönetici oluşturuldu: {User}. Şifreyi panelden değiştirin.", user.UserName);
    }
}
