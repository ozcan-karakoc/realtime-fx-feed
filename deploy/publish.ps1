<#
  FileZilla / FTP ile yüklenecek yayın paketini hazırlar (IIS, Windows hosting).

  Sonuç: publish\vitevitrin\  → bu klasörün İÇERİĞİ sitenin kök klasörüne (httpdocs / wwwroot) yüklenir.
  API (ASP.NET Core 8) ve React sitesi tek uygulamadır: aynı alan adında /, /admin, /api, /hubs, /ws birlikte çalışır.

  Kullanım:
    powershell -ExecutionPolicy Bypass -File deploy\publish.ps1
    powershell -ExecutionPolicy Bypass -File deploy\publish.ps1 -Domain www.vitevitrin.com -NoDemoData

  -NoDemoData : demo marjlar/içerik/ekranlar/fotoğraflar eklenmez (boş kurulum).
  -Update     : güncelleme paketi; App_Data ve appsettings.Production.json eklenmez (sunucudaki veriler ezilmez).
#>
param(
    [string]$Domain = "www.vitevitrin.com",
    [switch]$NoDemoData,
    [switch]$Update
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root "publish\vitevitrin"
$api = Join-Path $root "src\OzcanDoviz.Api"
$web = Join-Path $root "frontend\ozcan-doviz-web"

Write-Host "1/5 Arayüz derleniyor (React → src\OzcanDoviz.Api\wwwroot)..." -ForegroundColor Cyan
Push-Location $web
if (-not (Test-Path node_modules)) { npm ci }
npm run build
if ($LASTEXITCODE -ne 0) { throw "Arayüz derlenemedi." }
Pop-Location

# Taşınabilir (RID yok). SQLite, Windows'un winsqlite3.dll dosyasıdır; e_sqlite3.dll pakete girmez.
Write-Host "2/5 API yayınlanıyor (Release, taşınabilir, framework-dependent)..." -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $api "OzcanDoviz.Api.csproj") -c Release -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "Yayınlama başarısız." }

# Geliştirme ayarları sunucuya gitmez.
Remove-Item (Join-Path $out "appsettings.Development.json") -ErrorAction SilentlyContinue
# Yalnızca Windows yerel kitaplıkları kalır (Linux/macOS klasörleri paketi büyütür).
Get-ChildItem (Join-Path $out "runtimes") -Directory |
    Where-Object { $_.Name -notin "win", "win-x86", "win-x64", "win-arm64" } |
    Remove-Item -Recurse -Force
# Eski paketlerden kalan yerel SQLite dosyası IIS'te kilitleniyordu; winsqlite3 kullanıldığı için silinir.
Get-ChildItem $out -Recurse -Filter e_sqlite3.dll -ErrorAction SilentlyContinue | Remove-Item -Force

# IIS için web.config: ortam Production, stdout günlüğü logs\ klasörüne (sorun giderirken true yapın).
$webConfig = Join-Path $out "web.config"
[xml]$xml = Get-Content $webConfig
$aspnet = $xml.configuration.location.'system.webServer'.aspNetCore
$aspnet.SetAttribute("stdoutLogFile", ".\logs\stdout")
$envs = $xml.CreateElement("environmentVariables")
$env = $xml.CreateElement("environmentVariable"); $env.SetAttribute("name", "ASPNETCORE_ENVIRONMENT"); $env.SetAttribute("value", "Production")
[void]$envs.AppendChild($env); [void]$aspnet.AppendChild($envs)
$xml.Save($webConfig)
New-Item -ItemType Directory -Force (Join-Path $out "logs") | Out-Null

if (-not $Update) {
    Write-Host "3/5 Üretim ayarları oluşturuluyor..." -ForegroundColor Cyan
    # Windows PowerShell 5.1 uyumlu: kriptografik rastgele baytlar.
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 48; $rng.GetBytes($bytes)
    $jwtKey = [Convert]::ToBase64String($bytes)
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789"
    $pwBytes = New-Object byte[] 14; $rng.GetBytes($pwBytes)
    $pw = -join ($pwBytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
    $bare = $Domain -replace '^www\.', ''
    # Demo hesabı müşterilere gönderilir: şifre paketler arasında değişmesin diye dosyada saklanır (yüklenmez, git'e girmez).
    $demoNote = Join-Path (Split-Path $out -Parent) "DEMO-HESAP.txt"
    if (Test-Path $demoNote) {
        $demoPw = ((Get-Content $demoNote -Encoding UTF8) -match '^Şifre : ' | Select-Object -First 1) -replace '^Şifre : ', ''
    } else {
        $demoBytes = New-Object byte[] 10; $rng.GetBytes($demoBytes)
        $demoPw = -join ($demoBytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
        @"
$Domain demo hesabı (salt okunur)
Adres : https://$Domain/admin
Kullanıcı: demo
Şifre : $demoPw
"@ | Set-Content $demoNote -Encoding UTF8
    }
    $settings = [ordered]@{
        AllowedHosts      = "$bare;www.$bare;localhost"
        ConnectionStrings = @{ Default = "Data Source=App_Data/ozcandoviz.db" }
        Database          = @{ Provider = "Sqlite" }
        Jwt               = @{ Key = $jwtKey; ExpiresHours = 12 }
        Admin             = @{ UserName = "admin"; Password = $pw }
        Demo              = @{ UserName = "demo"; Password = $demoPw }
        Logging           = @{ LogLevel = @{ Default = "Warning"; "Microsoft.Hosting.Lifetime" = "Information" } }
    }
    $settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out "appsettings.Production.json") -Encoding UTF8

    # İlk giriş bilgisi pakete DEĞİL, yanına yazılır (FTP ile yüklenmez).
    $note = Join-Path (Split-Path $out -Parent) "ILK-GIRIS.txt"
    @"
$Domain ilk yönetici girişi
Adres : https://$Domain/admin
Kullanıcı: admin
Şifre : $pw

İlk girişte Kullanıcı Yönetimi → Şifremi Değiştir ile değiştirin.
Bu dosyayı kimseyle paylaşmayın; şifreyi değiştirdikten sonra silin.
"@ | Set-Content $note -Encoding UTF8

    $data = Join-Path $out "App_Data"
    New-Item -ItemType Directory -Force $data | Out-Null
    if (-not $NoDemoData) {
        Write-Host "4/5 Demo verileri ekleniyor..." -ForegroundColor Cyan
        $src = Join-Path $api "App_Data"
        # Müşteri verisi (sabitlemeler, bildirimler), Instagram anahtarı ve yerel veritabanı EKLENMEZ.
        foreach ($f in "margins.json", "settings.json", "content.json", "displays.json", "providers.json", "history-cache.json") {
            if (Test-Path (Join-Path $src $f)) { Copy-Item (Join-Path $src $f) $data }
        }
        if (Test-Path (Join-Path $src "uploads")) { Copy-Item (Join-Path $src "uploads") (Join-Path $data "uploads") -Recurse }
    } else {
        Write-Host "4/5 Demo verisi eklenmedi (boş kurulum)." -ForegroundColor Yellow
    }
} else {
    Write-Host "3-4/5 Güncelleme paketi: App_Data ve appsettings.Production.json eklenmedi." -ForegroundColor Yellow
}

Write-Host "5/5 Hazır." -ForegroundColor Green
$size = "{0:N1} MB" -f ((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "Klasör: $out ($size)"
if (-not $Update) { Write-Host "İlk giriş bilgisi: $(Join-Path (Split-Path $out -Parent) 'ILK-GIRIS.txt')" }
Write-Host "FileZilla ile bu klasörün İÇERİĞİNİ sitenin kök klasörüne yükleyin (bkz. deploy\YAYIN.md)."
