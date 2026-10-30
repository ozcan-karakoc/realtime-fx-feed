using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;

namespace OzcanDoviz.Api;

/// <summary>
/// Plesk'te httpdocs çoğu zaman yazmaya kapalıdır. Önce yapılandırılmış klasöre (App_Data) yazmayı dener;
/// olmazsa sürecin yazabildiği bir klasöre geçer ve FTP ile gelmiş dosyaları oraya kopyalar.
/// Hiçbir klasör yazılamıyorsa açılış yine durur.
/// </summary>
static class DataDirectoryBootstrap
{
    public static void EnsureWritable(WebApplicationBuilder builder)
    {
        var contentRoot = builder.Environment.ContentRootPath;
        var configured = builder.Configuration["Storage:DataDirectory"] ?? "App_Data";
        var preferred = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRoot, configured));

        var chosen = FirstWritable(Candidates(contentRoot, preferred))
            ?? throw new UnauthorizedAccessException($"App_Data klasörüne yazılamıyor: {preferred}");

        if (!SamePath(chosen, preferred))
        {
            CopyMissing(preferred, chosen);
            builder.Configuration["Storage:DataDirectory"] = chosen;
            var media = builder.Configuration["Media:Directory"];
            if (string.IsNullOrWhiteSpace(media) || !Path.IsPathRooted(media))
                builder.Configuration["Media:Directory"] = Path.Combine(chosen, "uploads");
            Console.Error.WriteLine($"App_Data yazılamıyor ({preferred}). Veriler şuraya yazılıyor: {chosen}");
        }

        if (string.Equals(builder.Configuration["Database:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
            return;

        var sqlite = new SqliteConnectionStringBuilder(
            builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/ozcandoviz.db");
        if (Path.IsPathRooted(sqlite.DataSource))
            return;

        sqlite.DataSource = SamePath(chosen, preferred)
            ? Path.GetFullPath(Path.Combine(contentRoot, sqlite.DataSource))
            : Path.Combine(chosen, Path.GetFileName(sqlite.DataSource));
        builder.Configuration["ConnectionStrings:Default"] = sqlite.ToString();
    }

    private static IEnumerable<string> Candidates(string contentRoot, string preferred)
    {
        yield return preferred;
        yield return Path.Combine(contentRoot, "data");

        var parent = Directory.GetParent(contentRoot)?.FullName;
        if (!string.IsNullOrEmpty(parent))
            yield return Path.Combine(parent, "data");

        foreach (var folder in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData })
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(root))
                yield return Path.Combine(root, "Vitevitrin");
        }

        var temp = Path.GetTempPath();
        if (!string.IsNullOrEmpty(temp))
            yield return Path.Combine(temp, "Vitevitrin");
    }

    private static string? FirstWritable(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in candidates)
        {
            string full;
            try { full = Path.GetFullPath(raw); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }

            if (!seen.Add(full)) continue;
            if (CanWrite(full)) return full;
        }
        return null;
    }

    private static bool CanWrite(string dir)
    {
        if (Probe(dir)) return true;
        TryGrantModify(dir);
        return Probe(dir);
    }

    private static bool Probe(string dir)
    {
        var probe = Path.Combine(dir, "yazma-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(dir);
            var info = new DirectoryInfo(dir);
            if (info.Exists && (info.Attributes & FileAttributes.ReadOnly) != 0)
                info.Attributes &= ~FileAttributes.ReadOnly;
            File.WriteAllText(probe, "ok");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void TryGrantModify(string dir)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(dir)) return;
        try
        {
            var info = new DirectoryInfo(dir);
            var security = info.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null) return;
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or IdentityNotMappedException)
        {
        }
    }

    private static void CopyMissing(string source, string destination)
    {
        if (!Directory.Exists(source) || SamePath(source, destination)) return;
        try
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                var name = Path.GetFileName(file);
                // Sunucuda kalmış yazma denemesi (yazma-testi) ve geçici dosyalar kopyalanmaz.
                if (name.Contains("yazma-testi", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                var target = Path.Combine(destination, name);
                if (!File.Exists(target)) File.Copy(file, target);
            }
            foreach (var sub in Directory.EnumerateDirectories(source))
                CopyMissing(sub, Path.Combine(destination, Path.GetFileName(sub)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Veri dosyaları kopyalanamadı ({source} → {destination}): {ex.Message}");
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
