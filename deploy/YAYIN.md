# Yayına alma (FileZilla + Windows/IIS hosting)

Site (React) ve API (ASP.NET Core 8) **tek uygulamadır**; aynı alan adında birlikte çalışır:

| Adres | Ne |
|---|---|
| `https://www.vitevitrin.com/` | Web sitesi (Anasayfa, Canlı Piyasalar, Geçmiş Kurlar, …) |
| `https://www.vitevitrin.com/admin` | Yönetim paneli |
| `https://www.vitevitrin.com/api/v1/...` | REST API |
| `wss://www.vitevitrin.com/hubs/prices` | Site için canlı fiyat (SignalR) |
| `wss://www.vitevitrin.com/ws/prices` | Mobil uygulamalar için WebSocket |
| `https://www.vitevitrin.com/ekran/{id}` | TV / LED pano ekranları |

## 1. Paketi hazırla

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publish.ps1 -Domain www.vitevitrin.com
```

Sonuç:

- `publish\vitevitrin\` → **yüklenecek klasör** (~20 MB)
- `publish\ILK-GIRIS.txt` → yönetici şifresi (**yüklenmez**, kimseyle paylaşmayın)

Seçenekler: `-NoDemoData` (boş kurulum), `-Update` (güncelleme; sunucudaki verilere dokunmaz, bkz. 5).

## 2. Hosting gereksinimleri (Plesk / IIS)

1. **ASP.NET Core 8 Hosting Bundle** kurulu olmalı (Plesk: *ASP.NET Core* bileşeni). Yoksa site `500.31` hatası verir.
2. Uygulama havuzu: **.NET CLR sürümü = "Yönetilen kod yok" (No Managed Code)**.
3. **WebSocket protokolü açık** olmalı (Plesk → IIS Ayarları / Sunucu rolü). Kapalıysa site yine çalışır, canlı fiyat uzun yoklamaya (long polling) düşer.
4. **SSL** sertifikası (Let's Encrypt) — `https://` zorunlu önerilir.
5. `App_Data` klasörüne uygulama havuzu kullanıcısının **yazma izni** (Plesk → Dosya Yöneticisi → `App_Data` → İzinler → *Write/Modify*). Marjlar, ayarlar, fotoğraflar ve veritabanı buradadır.

## 3. FileZilla ile yükleme

1. FileZilla → Dosya → Site Yöneticisi → hosting firmasının verdiği FTP bilgileri (tercihen **FTPS / FTP over TLS**).
2. Sağ tarafta sitenin kök klasörünü açın (Plesk'te genelde `httpdocs`, bazı firmalarda `wwwroot`).
3. Sol tarafta `publish\vitevitrin` klasörünün **içine** girin, tüm dosya ve klasörleri seçip sağa sürükleyin. (Klasörün kendisini değil, **içeriğini** yükleyin: kökte `web.config` ve `OzcanDoviz.Api.dll` görünmeli.)
4. Aktarım türü *Otomatik* veya *İkili (Binary)* olmalı.
5. Yükleme bitince `https://www.vitevitrin.com/health` → `{"status":"ok",...}` görmelisiniz.

## 4. İlk giriş

`https://www.vitevitrin.com/admin` → kullanıcı `admin`, şifre `publish\ILK-GIRIS.txt` içinde.
Girişten sonra **Kullanıcılar → Şifremi değiştir** ile değiştirin ve `ILK-GIRIS.txt` dosyasını silin.

## Demo hesabı (müşterilere gönderilir)

Rol **Demo**: paneli gezer, hiçbir şey kaydedemez; Kullanıcı Yönetimi ve İşlem İzi'ni görmez, müşteri ad ve telefonları maskelidir.

- Yeni kurulum: paketle otomatik açılır (`demo`). Şifre `publish\DEMO-HESAP.txt` dosyasındadır, paketler arasında değişmez.
- Çalışan site: yönetici hesabıyla **Kullanıcı Yönetimi → Yeni Kullanıcı** → kullanıcı `demo`, şifre `DEMO-HESAP.txt` içindeki, rol **Demo**.
- Kapatmak için: Kullanıcı Yönetimi → **Pasif yap**.
- `publish\tanitim` içindeki broşürlerde bu şifre yazar; o klasörü herkese açık depoya koymayın.

## 5. Güncelleme

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publish.ps1 -Update
```

1. Önce siteyi durdurun: kök klasöre `app_offline.htm` adlı boş bir dosya yükleyin (IIS uygulamayı kapatır, DLL'ler kilitlenmez).
2. `publish\vitevitrin` içeriğini yükleyin, "üzerine yaz" deyin. **`App_Data` ve `appsettings.Production.json` pakette yoktur**, sunucudakiler korunur.
3. `app_offline.htm` dosyasını silin.

## 6. Sorun giderme

| Belirti | Çözüm |
|---|---|
| `HTTP Error 500.30 / 500.31` | Hosting Bundle (.NET 8) eksik veya uygulama başlarken hata. `web.config` içinde `stdoutLogEnabled="true"` yapın, `logs\` klasöründeki dosyaya bakın, sonra tekrar `false` yapın. |
| `400 Bad Request` | Alan adı `appsettings.Production.json` → `AllowedHosts` içinde değil. Farklı bir alan adı için listeye ekleyin (`;` ile). |
| Panelde kaydet hatası | `App_Data` yazma izni yok (bkz. 2.5). |
| Fiyatlar akmıyor | `/health` çıktısındaki `error` alanına bakın; panel → Sağlayıcılar'dan başka kaynak seçilebilir. |

## 7. Veritabanı

Varsayılan SQLite: `App_Data\ozcandoviz.db` (kullanıcılar, API istemcileri, denetim kaydı) — ek kurulum gerekmez.
SQL Server kullanmak için `appsettings.Production.json`:

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": { "Default": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True" }
```

Tablolar ilk açılışta otomatik oluşturulur.
