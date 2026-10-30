using System.Net;
using Microsoft.Data.Sqlite;

namespace OzcanDoviz.Api;

/// <summary>
/// Uygulama açılırken hata verirse IIS'in boş "500.30" sayfası yerine nedeni ve çözümü gösteren
/// küçük bir uygulama çalışır. Hata ayrıca stdout'a ve (yazılabiliyorsa) logs\startup-error.txt'ye yazılır.
/// </summary>
public static class StartupFailure
{
    public static WebApplication Build(string[] args, Exception error)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        var (title, hint) = Explain(error);
        var root = app.Environment.ContentRootPath;
        var detail = $"{error.GetBaseException().GetType().Name}: {error.GetBaseException().Message}";

        Console.Error.WriteLine($"Uygulama başlatılamadı: {title}\n{error}");
        try
        {
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "startup-error.txt"), $"{DateTime.Now:O}\n{title}\n{hint}\n\n{error}");
        }
        catch { /* logs klasörü de yazılamıyor olabilir; sayfa yine de gösterilir */ }

        var html = $$"""
            <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Site başlatılamadı</title>
            <style>body{font-family:system-ui,Segoe UI,sans-serif;background:#0f1a2e;color:#e8edf7;margin:0;padding:24px}
            main{max-width:720px;margin:8vh auto;background:#16233f;border:1px solid #2a3a5e;border-radius:14px;padding:28px}
            h1{color:#f3d27a;font-size:22px;margin:0 0 12px}p{line-height:1.6}code{background:#0b1426;padding:2px 6px;border-radius:5px}
            .d{margin-top:18px;font-size:13px;color:#9fb0cf;word-break:break-word}</style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{hint}}</p>
            <p class="d">Ayrıntı: {{WebUtility.HtmlEncode(detail)}}<br>Uygulama klasörü: <code>{{WebUtility.HtmlEncode(root)}}</code></p>
            <p class="d">Sorunu giderdikten sonra uygulama havuzunu yeniden başlatın (veya web.config dosyasını yeniden yükleyin).</p></main></body></html>
            """;
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(html);
        });
        return app;
    }

    public static (string Title, string Hint) Explain(Exception error)
    {
        var all = Flatten(error).ToList();
        if (all.Any(e => e is UnauthorizedAccessException
                || e is SqliteException { SqliteErrorCode: 8 or 14 }  // salt okunur / dosya açılamıyor
                || (e is IOException && e.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))))
            return ("App_Data klasörüne yazılamıyor",
                "Uygulama havuzu kullanıcısının <code>App_Data</code> klasörüne <b>yazma/değiştirme</b> izni olmalı. " +
                "Plesk: <i>Web sitesi → Barındırma Ayarları → Ek yazma/değiştirme izinleri</i> kutusunu işaretleyin " +
                "veya <i>Dosya Yöneticisi → App_Data → İzinleri değiştir</i> ile uygulama havuzu kullanıcısına Değiştirme izni verin.");
        if (all.Any(IsFileLocked))
            return ("Site dosyası kilitli",
                "Yükleme sırasında bir dosya açık kalmış (çoğunlukla eski SQLite kitaplığı). Kök klasöre boş bir " +
                "<code>app_offline.htm</code> yükleyin, 10 saniye bekleyin, paketi yeniden yükleyin ve " +
                "<code>app_offline.htm</code> dosyasını silin. Uygulama havuzu 64 bit kalsın.");
        if (all.Any(e => e is BadImageFormatException))
            return ("Yerel kitaplık yüklenemedi (32/64 bit)",
                "Uygulama havuzunun bit ayarı ile paket uyuşmuyor. Plesk: <i>Barındırma Ayarları → Uygulama havuzu → " +
                "32 bit uygulamaları etkinleştir</i> ayarını kapatın.");
        if (all.Any(e => e is DllNotFoundException))
            return ("SQLite kitaplığı yüklenemedi",
                "Uygulama Windows'un <code>winsqlite3.dll</code> dosyasını kullanır. Sunucu Windows Server 2016 veya daha yeni olmalı. " +
                "Güncel <code>publish\\vitevitrin</code> paketini yükleyin.");
        if (all.Any(e => e.Message.Contains("Jwt", StringComparison.OrdinalIgnoreCase)))
            return ("Üretim ayarları eksik",
                "<code>appsettings.Production.json</code> dosyası yüklenmemiş veya JWT anahtarı eksik. Paketteki dosyayı sitenin köküne yükleyin.");
        return ("Site başlatılamadı",
            "Uygulama açılırken beklenmeyen bir hata oluştu. Ayrıntı aşağıda; tamamı <code>logs\\startup-error.txt</code> dosyasındadır.");
    }

    private static bool IsFileLocked(Exception e) =>
        e.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("0x80070020", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Exception> Flatten(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException)
        {
            yield return x;
            if (x is AggregateException agg)
                foreach (var inner in agg.InnerExceptions.SelectMany(Flatten)) yield return inner;
        }
    }
}
