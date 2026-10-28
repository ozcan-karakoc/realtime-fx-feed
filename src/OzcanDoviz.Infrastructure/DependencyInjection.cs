using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OzcanDoviz.Core.Content;
using OzcanDoviz.Core.Displays;
using OzcanDoviz.Core.Locks;
using OzcanDoviz.Infrastructure.Locks;
using OzcanDoviz.Infrastructure.Social;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Core.Providers;
using OzcanDoviz.Core.Rates;
using OzcanDoviz.Infrastructure.Persistence;
using OzcanDoviz.Infrastructure.Pricing;
using OzcanDoviz.Infrastructure.Providers;
using OzcanDoviz.Infrastructure.Rates;
using OzcanDoviz.Infrastructure.Storage;

namespace OzcanDoviz.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // SQL: yöneticiler, mobil istemci anahtarları, işlem izi. Online SQL Server için Database:Provider=SqlServer.
        var connection = configuration.GetConnectionString("Default") ?? "Data Source=App_Data/ozcandoviz.db";
        var dbProvider = configuration["Database:Provider"] ?? "Sqlite";
        services.AddDbContext<AppDbContext>(o =>
        {
            if (dbProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(5));
            else
                o.UseSqlite(connection);
        });

        // Marjlar ve sağlayıcı seçimi: yerel JSON.
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IMarginStore, MarginStore>();
        services.AddSingleton<IFeedSettingsStore, FeedSettingsStore>();

        // Site içeriği (kayan yazı, slaytlar, hakkımızda, iletişim) ve fotoğraflar: yerel dosya.
        services.AddSingleton<ISiteContentStore, SiteContentStore>();
        services.AddSingleton<IDisplayStore, DisplayStore>();
        services.Configure<MediaOptions>(configuration.GetSection("Media"));
        services.AddSingleton<MediaStorage>();
        services.AddSingleton<IMediaStorage>(sp => sp.GetRequiredService<MediaStorage>());

        // Instagram son gönderiler: typed client tekil tutulur (önbellek ve anahtar durumu süreç boyunca ortak).
        services.AddHttpClient(nameof(InstagramFeed), client =>
        {
            client.BaseAddress = new Uri(configuration["Instagram:GraphBaseUrl"] ?? "https://graph.instagram.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IInstagramFeed>(sp => ActivatorUtilities.CreateInstance<InstagramFeed>(sp,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(InstagramFeed))));

        // Fiyat sağlayıcıları.
        services.AddHttpClient(HttpProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OzcanDoviz/1.0");
        });
        services.AddSingleton<IPriceProvider, HaremSocketProvider>();
        services.AddSingleton<IPriceProvider, SaglamogluProvider>();
        services.AddSingleton<IPriceProvider, AhlatciProvider>();
        services.AddSingleton<IPriceProvider, OzbeyProvider>();
        services.AddSingleton<IPriceProvider, KitcoProvider>();
        services.AddSingleton<CustomProviderService>();
        services.AddSingleton<ICustomProviderService>(sp => sp.GetRequiredService<CustomProviderService>());
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<ProviderRegistry>());

        services.AddSingleton<IPriceBoard, PriceBoard>();
        services.AddSingleton<IRateLockStore, RateLockStore>();
        services.AddSingleton<IRateLockService, RateLockService>();
        services.AddSingleton<PriceRefreshSignal>();
        services.AddHostedService<PriceWorker>();

        // Harem geçmiş kur: typed client, yalnızca BaseUrl ayardan gelir. Cookie kullanılmaz.
        services.Configure<HaremAltinOptions>(configuration.GetSection(HaremAltinOptions.Section));
        services.AddHttpClient<IHaremAltinService, HaremAltinService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<HaremAltinOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new InvalidOperationException("HaremAltin:BaseUrl tanımlı değil.");
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.Referrer = new Uri(client.BaseAddress, "gecmis-kurlar?tip=1");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
        });

        return services;
    }
}
