# Özcan Döviz

Kuyumcu ve döviz bürosu için canlı fiyat sitesi + yönetim paneli.
Fiyat sağlayıcıdan (Harem, Sağlamoğlu, Ahlatçı, Özbey, Kitco) ham fiyat alınır, paneldeki marjlar uygulanır,
sonuç sitede, REST'te ve mobil uygulamalar için WebSocket'te yayınlanır.

- **Backend:** ASP.NET Core 8 (Controllers + SignalR + düz WebSocket + EF Core: SQL Server veya SQLite)
- **Frontend:** React 18 + TypeScript + Vite + SignalR istemcisi
- **Marjlar:** sunucu makinesinde `App_Data/margins.json` (her kayıtta önceki sürüm `App_Data/history/` altına kopyalanır)
- **SQL:** yöneticiler, mobil istemci anahtarları, işlem izi

## Çalıştırma

Gerekenler: .NET 8 SDK, Node 20+.

```bash
dotnet test
```

```bash
# API: http://localhost:5095 (Swagger: /swagger). wwwroot derlenmişse site de buradan açılır.
dotnet run --project src/OzcanDoviz.Api
```

```bash
# Arayüz geliştirme sunucusu: http://localhost:5191 (/api, /hubs, /ws API'ye yönlenir)
cd frontend/ozcan-doviz-web && npm install && npm run dev
```

```bash
# Üretim: arayüz API'nin wwwroot klasörüne derlenir, tek süreç her şeyi sunar
cd frontend/ozcan-doviz-web && npm run build
```

`dev.ps1` ikisini birlikte başlatır. Geliştirmede ilk yönetici `admin` / `admin1234` olarak açılır
(`appsettings.Development.json`). Panel: `/admin`.

### Üretim ayarları

| Ayar | Açıklama |
| --- | --- |
| `Database:Provider` | `SqlServer` (online SQL) veya `Sqlite` |
| `ConnectionStrings:Default` | SQL Server bağlantı dizesi |
| `Jwt:Key` | En az 32 karakter, gizli. Ortam değişkeni: `Jwt__Key` |
| `Admin:UserName`, `Admin:Password` | Yalnızca veritabanında hiç yönetici yokken ilk kullanıcıyı açar |
| `Storage:DataDirectory` | margins.json / settings.json klasörü (varsayılan `App_Data`) |
| `HaremAltin:BaseUrl` | Harem geçmiş kur adresi |
| `Cors:Origins` | Siteyi başka alan adından çağıracaksanız izinli kökenler |

Şema `EnsureCreated` ile kurulur. Tablolar değişirse EF migration'a geçin.

## Mimari

```
src/
  OzcanDoviz.Core            Sözleşmeler ve saf iş kuralı: IPriceProvider, marj modeli, PriceCalculator, Harem DTO
  OzcanDoviz.Infrastructure  Sağlayıcılar (Harem soketi, HTTP panolar), JSON marj deposu, EF Core, Harem geçmiş kur,
                             PriceWorker (arka plan fiyat döngüsü)
  OzcanDoviz.Api             Controller'lar, JWT, SignalR hub (/hubs/prices), mobil WebSocket (/ws/prices), SPA sunumu
tests/OzcanDoviz.Tests       Marj hesabı, Harem ayrıştırma (InvariantCulture), sağlayıcı ayrıştırıcıları
frontend/ozcan-doviz-web     Herkese açık fiyat ekranı + yönetim paneli
```

Akış:

```
Sağlayıcı (Harem wss / HTTP)  →  PriceWorker  →  PriceCalculator(margins.json)  →  IPriceBoard
                                       ↑ yedek sağlayıcı                              ├→ SignalR /hubs/prices  (site)
                                                                                      ├→ WebSocket /ws/prices  (mobil, anahtarlı)
                                                                                      └→ REST /api/v1/prices[/client]
```

- Harem socket.io bağlantısı süreçte tektir; kopunca 1→15 sn artan beklemeyle yeniden bağlanır.
- HTTP sağlayıcılar `PollSeconds` aralıkla okunur. Etkin sağlayıcı düşerse yedek sağlayıcıya geçilir;
  ikisi de düşerse son tablo "gecikmeli" işaretiyle yayında kalır.
- Her sağlayıcı ham kodun yanına ortak kod da yazar (`ALTIN` → `HASALTIN`, `CEYREK_YENI` → `CEYREK`, `USDTRY`…).
  Marj kuralları ortak koda bağlıdır; sağlayıcı değişince marjlar bozulmaz.
- Dışarı giden fiyatta kaynak (ham) fiyat **yoktur**; yalnızca yönetim paneli kaynak ile marjlı fiyatı yan yana görür.

## Marjlar (Fiyat Güncelleme Formu)

Her ürünün alış ve satışı için ayrı biçim:

| Biçim | Hesap | Örnek |
| --- | --- | --- |
| Katsayı × | kaynak × değer | 22 ayar alış = HAS alış × 0,9120 · Yeni çeyrek = HAS × 1,630 |
| TL + | kaynak + değer | USD satış = USDTRY satış + 0,10 |
| Yüzde % | kaynak × (1 + değer/100) | EUR alış = EURTRY alış × 0,99 |

Alış, kaynağın alışından; satış, kaynağın satışından hesaplanır. Sonuç ürünün ondalık ayarına yuvarlanır.
Kaynak sembolü etkin sağlayıcıda olmayan ürün yayında görünmez.

## Mobil / dış istemci WebSocket

Panel → **Mobil / WebSocket** → istemci oluştur. Anahtar (`odz_…`) bir kez gösterilir; sunucu yalnızca SHA-256 özetini saklar.
İsteğe bağlı ürün filtresi (ör. `USD,EUR,HAS`) yalnızca o ürünleri gönderir.

```
wss://alanadiniz/ws/prices?key=odz_…          (veya X-Api-Key başlığı)
GET https://alanadiniz/api/v1/prices/client?key=odz_…
```

Bağlanınca son tablo, sonra her değişim tek JSON mesaj olarak gelir:

```json
{
  "type": "prices",
  "provider": "Harem Altın",
  "updatedUtc": "2026-10-28T10:23:57Z",
  "stale": false,
  "items": [
    { "code": "USD", "name": "Amerikan Doları", "category": "doviz", "buy": 49.813, "sell": 49.88, "decimals": 4, "direction": "up" }
  ]
}
```

İstemci `ping` gönderirse `{"type":"pong"}` döner. Anahtar yenilenince veya istemci pasif yapılınca yeni bağlantılar reddedilir (401).

## Harem Altın geçmiş kur

```
GET /api/v1/rates/harem/history?date=2026-09-23
GET /api/v1/rates/harem/history?date=2026-09-23&code=ALTIN
```

- Harem'e `tarih=dd-MM-yyyy`, `tip=1`, `interval=gun` gönderilir; başlıklar typed HttpClient kaydındadır, cookie yoktur.
- `alis`/`satis` metinleri `InvariantCulture` ile çözülür (`6783.4300` → 6783,43).
- `error=true` başarı sayılmaz; HTTP hatası veya bozuk gövde `502` döner (`upstreamStatus` alanıyla).
- `recordDate`, Harem'in `kayit_tarihi` değeridir; istek tarihiyle değiştirilmez.
- Yönetici girişi gerekir.

## Yönetim paneli

| Sayfa | İş |
| --- | --- |
| Yönetim Paneli | Etkin sağlayıcı, kaynak → marjlı fiyat tablosu, bağlı mobil soketler |
| Fiyat Güncelleme | Marj formu (alış yeşil, satış kırmızı), ürün ekle/sil/sırala, canlı önizleme |
| Fiyat Sağlayıcıları | Etkin + yedek sağlayıcı, yoklama aralığı, dükkan adı, bağlantı testi |
| Mobil / WebSocket | İstemci anahtarı oluştur, filtrele, yenile, pasif yap |
| Geçmiş Kurlar | Harem geçmiş kur sorgusu |
| Kullanıcı Yönetimi | Yönetici ekle, şifre sıfırla/değiştir, pasif yap |
| İşlem İzi | Giriş, marj kaydı, sağlayıcı ve anahtar işlemleri |
