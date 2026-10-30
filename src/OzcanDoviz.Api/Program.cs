using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OzcanDoviz.Api.Auth;
using OzcanDoviz.Api.Realtime;
using OzcanDoviz.Core.Pricing;
using OzcanDoviz.Infrastructure;
using OzcanDoviz.Infrastructure.Persistence;
using OzcanDoviz.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// SQLite varsayılanı App_Data altındadır; klasör uygulama klasörüne göre açılır.
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["Storage:DataDirectory"] ?? "App_Data"));

// SQLite'ta göreli "Data Source" uygulama klasörüne göre çözülür: IIS'te çalışma klasörü farklı olabilir.
if (!string.Equals(builder.Configuration["Database:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/ozcandoviz.db");
    if (!Path.IsPathRooted(sqlite.DataSource))
    {
        sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
        builder.Configuration["ConnectionStrings:Default"] = sqlite.ToString();
    }
}

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
// SignalR da enum'ları metin yazsın (ör. sabitleme durumu "Completed"); REST ile aynı biçim.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<MobileSocketHub>();
builder.Services.AddSingleton<IPriceBroadcaster>(sp => sp.GetRequiredService<MobileSocketHub>());
builder.Services.AddSingleton<IPriceBroadcaster, SignalRBroadcaster>();

// Yönetici girişi: JWT.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton<TokenService>();
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey(),
        ClockSkew = TimeSpan.FromMinutes(1),
    });
builder.Services.AddAuthorization();

// Kaba kuvvete karşı: IP başına dakikada 10 giriş denemesi.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Mesaj botundan kur sabitleme: IP başına 10 dakikada RateLimits:LocksPer10Minutes (varsayılan 5) istek.
    var lockLimit = builder.Configuration.GetValue("RateLimits:LocksPer10Minutes", 5);
    o.AddPolicy("lock", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = lockLimit, Window = TimeSpan.FromMinutes(10) }));
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "DEMO Fiyat API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
// Panelden yüklenen fotoğraflar: App_Data/uploads → /media
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Services.GetRequiredService<MediaStorage>().Root),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800";
    },
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.MapControllers();
app.MapHub<PriceHub>(PriceHub.Path);
app.Map(MobileSocketHub.Path, (HttpContext ctx, MobileSocketHub hub, AppDbContext db) => hub.HandleAsync(ctx, db));
app.MapGet("/health", (IPriceBoard board) => Results.Ok(new
{
    status = board.Current.Items.Count > 0 ? "ok" : "waiting",
    board.Current.Provider,
    board.Current.UpdatedUtc,
    board.Current.Error,
}));

// Bilinmeyen API yolu gerçek 404'tür; SPA'nın index.html'i dönmez (istemci HTML'i JSON sanıp önbelleğe almasın).
// /media buraya eklenmez: bir uç eşleşirse statik dosya katmanı devre dışı kalır ve fotoğraflar sunulmaz.
// Olmayan fotoğraf zaten 404 döner (index.html geri dönüşü uzantılı yolları kapsamaz).
foreach (var prefix in new[] { "/api", "/hubs", "/ws" })
    app.MapFallback(prefix + "/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Adres bulunamadı."));

// React SPA: diğer yollar index.html'e düşer (wwwroot varsa).
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
