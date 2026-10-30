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

## Site sayfaları ve içerik

Site: **Anasayfa** (slayt fotoğrafları + öne çıkan fiyatlar), **Canlı Piyasalar**, **Hakkımızda**, **İletişim**.
Üstte kayan yazı (panelden metin, hız, canlı fiyat ekleme). İçerik `App_Data/content.json`, fotoğraflar
`App_Data/uploads` (yalnızca JPG/PNG/WebP, en fazla 5 MB, dosya imzası kontrol edilir) ve `/media/…` adresinden sunulur.
Panel → **Site İçeriği**'nden değişir; açık sayfalar ve mobil istemciler kaydedince hemen güncellenir.
Hakkımızda ve İletişim sekmelerinde **Demo içerik doldur** düğmesi vardır (yayına almadan önce gerçek bilgiyle değiştirin).

## Sosyal medya, logo ve Instagram

- **Sosyal Medya** (Site İçeriği): WhatsApp, Instagram, Telefon, Facebook, YouTube, Telegram. "Sağ altta göster" işaretliler
  asistan düğmesinin üstünde yuvarlak ikon olur; hepsi footer ve İletişim'de listelenir. Tıklanacak adres sunucuda üretilir
  (wa.me, instagram.com…); kullanıcı girdisi doğrudan bağlantı olmaz.
- **Logo ve Görseller**: header logosu (dükkan adı yerine) ve footer görseli.
- **Anasayfa**: tanıtım başlığı/metni, özellik kartları, rakamlar, alt çağrı bandı. İlk açılışta demo metinlerle gelir.
- **Instagram**: işletme/içerik üreticisi hesabının uzun ömürlü erişim anahtarıyla son gönderiler hero'nun altında gösterilir.
  Anahtar `App_Data/secrets.json` dosyasındadır (içerik dosyasında ve hiçbir cevapta yoktur), 30 günden eskisi otomatik yenilenir,
  liste 15 dk önbelleklenir. Anahtar yoksa ve Instagram adresi girilmişse "takip edin" kartı çıkar.
  Hikâyeler (story) bu API ile alınamaz; yalnızca gönderiler.

## TV / Pano / LED ekranları

Panel → **TV / Pano / LED**: ekran ekle (TV, dikey pano, LED-COM, LED-Bluetooth), ürünleri ve sırasını seç, önizle.
Tanımlar `App_Data/displays.json` dosyasındadır.

| Bağlantı | Nasıl |
| --- | --- |
| HDMI | Ekrana bağlı bilgisayarda/TV kutusunda `https://alanadi/ekran/{id}` tam ekran açılır. Tema, sütun, yazı ölçeği, dikey yön, sayfa döndürme, saat, logo, kayan yazı. Yazı boyu satır sayısına göre ekrana sığar. |
| COM | LED panele bağlı bilgisayarda Chrome/Edge ile `/ekran/{id}/gonder` → "COM porta bağlan". Baud, veri biti, eşlik, durma biti paneldedir. USB-seri, RS-232/485 ve Windows'ta sanal COM görünen klasik Bluetooth (SPP) kartlar. |
| Bluetooth (BLE) | Aynı sayfada "Bluetooth cihaza bağlan"; servis/karakteristik UUID ve paket boyu paneldedir. |

COM/Bluetooth çıktısı sunucuda üretilir (`GET /api/v1/displays/{id}/frame`, ham bayt): satır şablonu
(`{code,-8}{buy,10}{sell,10}`), başlık (`{time} {date}`), satır ayırıcı, başlangıç/bitiş baytları (ör. STX `02` / ETX `03`),
kodlama (ASCII, Windows-1254, ISO-8859-9, UTF-8), Türkçe harfleri ASCII'ye çevirme, büyük harf, sayı biçimi.
Gönderici aralıkla ve her fiyat değişiminde yazar. Web Serial/Bluetooth yalnızca https veya localhost'ta çalışır;
sunucu internette olsa bile cihaz dükkandaki tarayıcıdan sürülür.

## Geçmiş Kurlar (grafik + hesaplayıcı)

Site menüsü: Anasayfa · Canlı Piyasalar · **Geçmiş Kurlar** · Hakkımızda · İletişim. Panel → Site İçeriği → **Menü ve Sayfalar**'dan
sayfa gizlenebilir, menü adı değiştirilebilir, hesaplayıcı kapatılabilir (kapalıyken uçlar 404 döner).

- Grafik: 15 sembol (altın tip=1, döviz tip=0), 1 hafta / 1 ay / 3 ay, alış ve satış çizgisi, üzerine gelince gün değeri.
  Veri Harem Altın günlük kapanışıdır (marjsız piyasa verisi).
- Her gün için Harem'e bir istek; geçmiş günler `App_Data/history-cache.json`'da kalıcı, bugün 10 dk önbellekte.
  Hafta sonu tekrar eden kayıt günü bir kez sayılır.
- Hesaplayıcı: dükkanın canlı (marjlı) fiyatıyla birim ↔ TL; seçili aralığın başında alınsaydı bugünkü değeri.

```
GET /api/v1/history/codes
GET /api/v1/history/series?code=ALTIN&days=30
```

## Bildirim Paneli

Panel → **Bildirim Paneli**: başlık, mesaj, tür (bilgi/kampanya/uyarı/önemli), bağlantı ve kimlerin göreceği.
Gönderildiği an sitesi açık olan herkese SignalR ile düşer (panelde açık ekran sayısı görünür). Süre seçilirse o süre içinde
siteyi açanlar da görür. Geri çekilen bildirim açık ekranlardan kalkar. Kayıtlar `App_Data/notices.json`'da.

## Şubeler, haritalar ve görseller

Panel → Site İçeriği → İletişim: şubeler (ad, adres, harita araması, telefon, saat). Her şube Google haritasıyla
Anasayfa, Hakkımızda ve İletişim'de gösterilir; harita ve yol tarifi adresi sunucuda üretilir. Varsayılan demo şubeler:
Kapalıçarşı ve Kuyumcukent. Anasayfa tanıtım, Hakkımızda ve İletişim görselleri panelden yüklenir; yüklenmezse
`frontend/ozcan-doviz-web/public/demo` altındaki ücretsiz lisanslı fotoğraflar gösterilir (kaynaklar: `public/demo/CREDITS.md`).

## Mesaj botu ve kur sabitleme

Sitenin sağ altındaki asistan: fiyat sorma, **kur sabitleme**, referans sorgulama; ürün adı yazılınca fiyat gösterir.

- Fiyat istemciden alınmaz; sabitleme anındaki marjlı tablodan okunur. Müşteri alıyorsa satış, satıyorsa alış fiyatı.
- Süre (dakika), azami miktar ve açık/kapalı panel → **Kur Sabitlemeleri** sayfasından ayarlanır (`settings.json`).
- Her sabitlemeye `OD-YYMMDD-XXXXX` referansı verilir ve `App_Data/locks.json` dosyasına yazılır
  (180 günden eski kayıtlar budanır). Panelde **Tamamlandı / İptal** işaretlenir.
- Referansla sorguda müşteri adı maskelenir, telefon dönmez. IP başına 10 dakikada 5 sabitleme
  (`RateLimits:LocksPer10Minutes`).

```
GET  /api/v1/locks/config
POST /api/v1/locks          { productCode, side: "Buy"|"Sell", quantity, name, phone }
GET  /api/v1/locks/{ref}
```

## Kategori ve ürün bazında sağlayıcı

Panel → **Fiyat Sağlayıcıları**: her kategori (altın, döviz, parite, maden) için ayrı sağlayıcı seçilebilir
(ör. altın Sağlamoğlu, döviz Harem). **Fiyat Güncelleme**'deki ürün kartında tek bir ürün de ayrı sağlayıcıya bağlanabilir.
Öncelik: ürün → kategori → genel. Bir ürünün sağlayıcısı düşerse ya da sembolü yoksa önce yedek, sonra genel sağlayıcıdan hesaplanır.

## Kendi fiyat sağlayıcın (ws:// · wss:// · http)

Panel → Fiyat Sağlayıcıları → **Kendi Sağlayıcın**: anlaşmalı bir akışın adresini girin.

| Alan | Açıklama |
| --- | --- |
| Adres | `ws://`, `wss://` (canlı akış, bağlantı süreç boyunca açık, kopunca yeniden bağlanır) veya `http(s)://` (yoklama) |
| Protokol | Otomatik / düz JSON / socket.io (Engine.IO 4: el sıkışma, ping/pong, `42[...]` paketleri) |
| Abonelik mesajı, Origin | Bağlanınca gönderilecek mesaj ve gerekiyorsa Origin başlığı |
| Veri yolu | Fiyat listesinin yeri: `data`, `result.items`, socket.io için `1.data`. Liste ya da sembol→nesne sözlüğü olabilir |
| Sembol / alış / satış / ad alanı | Alan adları; iç içe alan noktayla (`quote.bid`). Sayılar "41,10" veya "41.10" olabilir |
| Ortak koda eşle | `ALTIN=HASALTIN` gibi; marj kuralları ortak koda bağlı olduğundan eşleme önerilir |

**Dene** kaydetmeden bağlanır, ilk fiyatlı mesajı (en fazla 10 sn) bekler, okunan sembolleri ve gelen ham mesajı gösterir.
Kaydedilen sağlayıcı hemen genel/yedek/kategori/ürün seçimlerinde görünür (yeniden başlatma gerekmez); tanımlar
`App_Data/providers.json` dosyasındadır. Kullanımdaki sağlayıcı silinemez. Sunucu girilen adrese bağlandığı için bu bölüm
yalnızca yöneticiye açıktır ve her deneme/kayıt işlem izine yazılır.

## Mobil servis (brokerId)

Panel → Site İçeriği → **Mobil Servis**'teki aracı kimliği (`brokerId`) ve istemci anahtarıyla (`X-Api-Key`):

```
GET /api/v1/mobile/{brokerId}            açılış paketi: aracı, fiyatlar, içerik, soket adresi
GET /api/v1/mobile/{brokerId}/prices
GET /api/v1/mobile/{brokerId}/content    fotoğraf adresleri tam (https://…/media/…)
```

Yanlış brokerId 404, geçersiz/pasif anahtar 401 döner. İçerik değişince sokete `{"type":"content-updated"}` gelir.

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
| Kur Sabitlemeleri | Botun sabitlemeleri (ref no, kalan süre), tamamla/iptal, süre ve azami miktar ayarı |
| Fiyat Güncelleme | Marj formu (alış yeşil, satış kırmızı), ürün bazında sağlayıcı, ekle/sil/sırala, canlı önizleme |
| Fiyat Sağlayıcıları | Genel + yedek sağlayıcı, kategori bazında sağlayıcı, yoklama aralığı, dükkan adı, bağlantı testi |
| Site İçeriği | Anasayfa tanıtımı, kayan yazı, slaytlar, logo/footer görseli, sosyal medya, Instagram, Hakkımızda, İletişim (demo doldurma), mobil brokerId |
| TV / Pano / LED | Ekran tasarımı, ürün seçimi, HDMI ekran adresi, COM/Bluetooth gönderici ve LED çıktı önizlemesi |
| Mobil / WebSocket | İstemci anahtarı oluştur, filtrele, yenile, pasif yap |
| Geçmiş Kurlar | Harem geçmiş kur sorgusu |
| Kullanıcı Yönetimi | Yönetici ekle, şifre sıfırla/değiştir, pasif yap |
| İşlem İzi | Giriş, marj kaydı, sağlayıcı ve anahtar işlemleri |
