import { useEffect, useState } from 'react'
import {
  api, ApiError, json,
  type Branding, type BranchLocation, type HomeSection, type InstagramPost, type InstagramSection, type InstagramStatus,
  type MediaFile, type SiteContent, type Slide, type SocialLink, type SocialType,
} from '../api'
import { SocialIcon } from '../site/SocialIcon'
import { dateTime } from '../format'

type Tab = 'pages' | 'home' | 'ticker' | 'slides' | 'branding' | 'social' | 'instagram' | 'about' | 'contact' | 'mobile'

const tabs: { key: Tab; label: string }[] = [
  { key: 'pages', label: 'Menü ve Sayfalar' },
  { key: 'home', label: 'Anasayfa' },
  { key: 'ticker', label: 'Kayan Yazı' },
  { key: 'slides', label: 'Slaytlar' },
  { key: 'branding', label: 'Logo ve Görseller' },
  { key: 'social', label: 'Sosyal Medya' },
  { key: 'instagram', label: 'Instagram' },
  { key: 'about', label: 'Hakkımızda' },
  { key: 'contact', label: 'İletişim' },
  { key: 'mobile', label: 'Mobil Servis' },
]

/**
 * Site İçeriği: kayan yazı, anasayfa fotoğrafları, Hakkımızda ve İletişim.
 * Kayıt sunucuda App_Data/content.json dosyasına yazılır; açık sayfalar ve mobil istemciler hemen güncellenir.
 */
export function SiteContentPage() {
  const [content, setContent] = useState<SiteContent | null>(null)
  const [media, setMedia] = useState<MediaFile[]>([])
  const [tab, setTab] = useState<Tab>('pages')
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string; details?: string[] } | null>(null)

  const loadMedia = () => api<MediaFile[]>('/api/v1/admin/media').then(setMedia)
  useEffect(() => {
    void api<SiteContent>('/api/v1/admin/content').then(setContent)
    void loadMedia()
  }, [])

  if (!content) return <div className="page"><div className="panel pad">Yükleniyor…</div></div>

  const patch = (p: Partial<SiteContent>) => { setContent({ ...content, ...p }); setDirty(true) }

  async function save() {
    setSaving(true)
    setMessage(null)
    try {
      const saved = await api<SiteContent>('/api/v1/admin/content', { method: 'PUT', body: json(content) })
      setContent(saved)
      setDirty(false)
      setMessage({ ok: true, text: 'Kaydedildi. Açık sayfalar ve mobil uygulamalar güncellendi.' })
    } catch (e) {
      const err = e as ApiError
      setMessage({ ok: false, text: err.message, details: err.details })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="page">
      <section className="panel form-panel">
        <div className="form-title">Site İçeriği</div>
        <div className="toolbar">
          <div className="tabs" role="tablist">
            {tabs.map((t) => (
              <button key={t.key} role="tab" aria-selected={tab === t.key} className={`tab ${tab === t.key ? 'active' : ''}`} onClick={() => setTab(t.key)}>
                {t.label}
              </button>
            ))}
          </div>
          <span className="grow" />
          <span className="muted small">Son kayıt: {dateTime(content.updatedUtc)}{content.updatedBy ? ` · ${content.updatedBy}` : ''}</span>
          {dirty && <span className="badge warn">Kaydedilmedi</span>}
          <button className="btn primary" onClick={save} disabled={saving || !dirty}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</button>
        </div>

        {message && (
          <div className={`alert ${message.ok ? 'ok' : 'error'}`}>
            {message.text}
            {message.details && message.details.length > 0 && <ul>{message.details.map((d) => <li key={d}>{d}</li>)}</ul>}
          </div>
        )}

        <div className="pad">
          {tab === 'pages' && <PagesTab content={content} patch={patch} />}
          {tab === 'home' && <HomeTab content={content} patch={patch} media={media} reloadMedia={loadMedia} />}
          {tab === 'branding' && <BrandingTab content={content} patch={patch} media={media} reloadMedia={loadMedia} />}
          {tab === 'social' && <SocialTab content={content} patch={patch} />}
          {tab === 'instagram' && <InstagramTab content={content} patch={patch} />}
          {tab === 'ticker' && <TickerTab content={content} patch={patch} />}
          {tab === 'slides' && <SlidesTab content={content} patch={patch} media={media} reloadMedia={loadMedia} />}
          {tab === 'about' && <AboutTab content={content} patch={patch} media={media} reloadMedia={loadMedia} />}
          {tab === 'contact' && <ContactTab content={content} patch={patch} media={media} reloadMedia={loadMedia} />}
          {tab === 'mobile' && <MobileTab content={content} patch={patch} />}
        </div>
      </section>
    </div>
  )
}

interface TabProps {
  content: SiteContent
  patch: (p: Partial<SiteContent>) => void
}

function PagesTab({ content, patch }: TabProps) {
  const p = content.pages
  const set = (x: Partial<SiteContent['pages']>) => patch({ pages: { ...p, ...x } })
  return (
    <div>
      <p className="muted">Site menüsü: Anasayfa · Canlı Piyasalar · <b>{p.history ? p.historyTitle || 'Geçmiş Kurlar' : '(gizli)'}</b> · Hakkımızda · İletişim</p>
      <div className="page-toggle">
        <label className="switch"><input type="checkbox" checked={p.history} onChange={(e) => set({ history: e.target.checked })} /><span /></label>
        <div className="grow">
          <strong>Geçmiş Kurlar sayfası</strong>
          <div className="muted small">Açıksa menüde görünür: grafikli geçmiş kurlar (1 hafta / 1 ay / 3 ay). Kapalıysa menüden kalkar ve sayfa açılmaz.</div>
        </div>
        <label className="field"><span>Menü adı</span><input value={p.historyTitle} maxLength={40} disabled={!p.history} onChange={(e) => set({ historyTitle: e.target.value })} /></label>
      </div>
      <div className="page-toggle">
        <label className="switch"><input type="checkbox" checked={p.calculator} disabled={!p.history} onChange={(e) => set({ calculator: e.target.checked })} /><span /></label>
        <div className="grow">
          <strong>Hesaplayıcı</strong>
          <div className="muted small">Geçmiş Kurlar sayfasının altında: güncel fiyatımızla birim ↔ TL ve seçili aralıkta getiri hesabı.</div>
        </div>
      </div>
    </div>
  )
}

function HomeTab({ content, patch, media, reloadMedia }: TabProps & { media: MediaFile[]; reloadMedia: () => Promise<void> }) {
  const h = content.home
  const set = (p: Partial<HomeSection>) => patch({ home: { ...h, ...p } })
  return (
    <div>
      <DemoBar onFill={() => set(demoHome)} />
      <label className="field"><span>Tanıtım başlığı</span><input value={h.introTitle} maxLength={120} onChange={(e) => set({ introTitle: e.target.value })} /></label>
      <label className="field"><span>Tanıtım metni</span><textarea rows={4} maxLength={2000} value={h.introText} onChange={(e) => set({ introText: e.target.value })} /></label>
      <div className="field">
        <span>Tanıtım görseli (boşsa demo görsel)</span>
        <div className="row-actions wrap">
          <div className="logo-preview"><img src={h.introImageUrl || '/demo/intro.jpg'} alt="" /></div>
          <MediaPicker media={media} value={h.introImageUrl ?? ''} allowEmpty onChange={(url) => set({ introImageUrl: url || null })} />
          <Uploader label="Yeni görsel yükle" onUploaded={async (f) => { await reloadMedia(); set({ introImageUrl: f.url }) }} />
        </div>
      </div>

      <h3 className="sub-title">Özellik kartları (en fazla 8)</h3>
      <div className="list-edit">
        {h.features.map((f, i) => (
          <div key={i} className="list-row">
            <input className="icon-input" value={f.icon} maxLength={4} onChange={(e) => set({ features: h.features.map((x, j) => (j === i ? { ...x, icon: e.target.value } : x)) })} aria-label="Simge" />
            <input className="grow-input" value={f.title} maxLength={60} placeholder="Başlık" onChange={(e) => set({ features: h.features.map((x, j) => (j === i ? { ...x, title: e.target.value } : x)) })} />
            <input className="grow-input wide" value={f.text} maxLength={200} placeholder="Açıklama" onChange={(e) => set({ features: h.features.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)) })} />
            <button className="icon-btn small danger" onClick={() => set({ features: h.features.filter((_, j) => j !== i) })} title="Sil">✕</button>
          </div>
        ))}
      </div>
      <button className="btn" disabled={h.features.length >= 8} onClick={() => set({ features: [...h.features, { icon: '★', title: '', text: '' }] })}>+ Kart ekle</button>

      <h3 className="sub-title">Rakamlar (en fazla 6)</h3>
      <div className="list-edit">
        {h.stats.map((s, i) => (
          <div key={i} className="list-row">
            <input className="grow-input" value={s.value} maxLength={20} placeholder="25+" onChange={(e) => set({ stats: h.stats.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) })} />
            <input className="grow-input wide" value={s.label} maxLength={60} placeholder="Yıllık tecrübe" onChange={(e) => set({ stats: h.stats.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)) })} />
            <button className="icon-btn small danger" onClick={() => set({ stats: h.stats.filter((_, j) => j !== i) })} title="Sil">✕</button>
          </div>
        ))}
      </div>
      <button className="btn" disabled={h.stats.length >= 6} onClick={() => set({ stats: [...h.stats, { value: '', label: '' }] })}>+ Rakam ekle</button>

      <h3 className="sub-title">Alt çağrı bandı</h3>
      <div className="form-row">
        <label className="field"><span>Başlık</span><input value={h.ctaTitle} maxLength={120} onChange={(e) => set({ ctaTitle: e.target.value })} /></label>
        <label className="field grow2"><span>Metin</span><input value={h.ctaText} maxLength={1000} onChange={(e) => set({ ctaText: e.target.value })} /></label>
      </div>
    </div>
  )
}

function BrandingTab({ content, patch, media, reloadMedia }: TabProps & { media: MediaFile[]; reloadMedia: () => Promise<void> }) {
  const b = content.branding
  const set = (p: Partial<Branding>) => patch({ branding: { ...b, ...p } })
  const slot = (key: keyof Branding, label: string, hint: string) => (
    <div className="field">
      <span>{label}</span>
      <div className="row-actions wrap">
        <div className="logo-preview">{b[key] ? <img src={b[key]!} alt="" /> : <span className="muted small">Görsel yok</span>}</div>
        <MediaPicker media={media} value={b[key] ?? ''} allowEmpty onChange={(url) => set({ [key]: url || null })} />
        <Uploader label="Yeni görsel yükle" onUploaded={async (f) => { await reloadMedia(); set({ [key]: f.url }) }} />
      </div>
      <span className="muted small">{hint}</span>
    </div>
  )
  return (
    <div>
      {slot('headerLogoUrl', 'Header logosu', 'Üst menüde dükkan adının yerine gösterilir. Şeffaf PNG, yaklaşık 400×100 önerilir.')}
      {slot('footerImageUrl', 'Footer görseli', 'Sayfa altında gösterilir (logo veya kurumsal görsel).')}
    </div>
  )
}

const socialTypes: { type: SocialType; label: string; placeholder: string }[] = [
  { type: 'WhatsApp', label: 'WhatsApp', placeholder: '90 5xx xxx xx xx' },
  { type: 'Instagram', label: 'Instagram', placeholder: '@kullaniciadi' },
  { type: 'Phone', label: 'Telefon', placeholder: '0212 000 00 00' },
  { type: 'Facebook', label: 'Facebook', placeholder: 'https://www.facebook.com/…' },
  { type: 'YouTube', label: 'YouTube', placeholder: 'https://www.youtube.com/@…' },
  { type: 'Telegram', label: 'Telegram', placeholder: '@kanal' },
]

function SocialTab({ content, patch }: TabProps) {
  const list = content.social
  const setAt = (i: number, p: Partial<SocialLink>) => patch({ social: list.map((s, j) => (j === i ? { ...s, ...p } : s)) })
  return (
    <div>
      <p className="muted">"Sağ altta göster" işaretli bağlantılar sitenin sağ alt köşesinde, asistan düğmesinin üstünde yuvarlak ikon olarak çıkar. Tümü footer ve İletişim sayfasında da listelenir.</p>
      <div className="list-edit">
        {list.map((s, i) => {
          const meta = socialTypes.find((t) => t.type === s.type)!
          return (
            <div key={i} className="social-edit">
              <span className={`float-btn small ${s.type.toLowerCase()}`}><SocialIcon type={s.type} size={18} /></span>
              <select value={s.type} onChange={(e) => setAt(i, { type: e.target.value as SocialType })}>
                {socialTypes.map((t) => <option key={t.type} value={t.type}>{t.label}</option>)}
              </select>
              <input className="grow-input" value={s.value} placeholder={meta.placeholder} onChange={(e) => setAt(i, { value: e.target.value })} />
              {s.type === 'WhatsApp' && (
                <input className="grow-input" value={s.message ?? ''} maxLength={200} placeholder="Hazır mesaj (isteğe bağlı)" onChange={(e) => setAt(i, { message: e.target.value || null })} />
              )}
              <label className="mini check"><input type="checkbox" checked={s.floating} onChange={(e) => setAt(i, { floating: e.target.checked })} /> Sağ altta göster</label>
              <button className="icon-btn small danger" onClick={() => patch({ social: list.filter((_, j) => j !== i) })} title="Sil">✕</button>
            </div>
          )
        })}
      </div>
      <div className="row-actions wrap">
        {socialTypes.map((t) => (
          <button key={t.type} className="btn small" disabled={list.length >= 12}
            onClick={() => patch({ social: [...list, { type: t.type, value: '', floating: t.type === 'WhatsApp' || t.type === 'Instagram' }] })}>
            + {t.label}
          </button>
        ))}
      </div>
    </div>
  )
}

function InstagramTab({ content, patch }: TabProps) {
  const ig = content.instagram
  const set = (p: Partial<InstagramSection>) => patch({ instagram: { ...ig, ...p } })
  const [status, setStatus] = useState<InstagramStatus | null>(null)
  const [posts, setPosts] = useState<InstagramPost[]>([])
  const [token, setToken] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = async () => {
    setStatus(await api<InstagramStatus>('/api/v1/admin/instagram'))
    setPosts(await api<InstagramPost[]>('/api/v1/admin/instagram/posts').catch(() => []))
  }
  useEffect(() => { void load() }, [])

  async function saveToken() {
    setBusy(true)
    setError(null)
    try {
      setStatus(await api<InstagramStatus>('/api/v1/admin/instagram/token', { method: 'PUT', body: json({ token }) }))
      setToken('')
      setPosts(await api<InstagramPost[]>('/api/v1/admin/instagram/posts'))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <div className="form-row">
        <label className="mini check big"><input type="checkbox" checked={ig.enabled} onChange={(e) => set({ enabled: e.target.checked })} /> Anasayfada göster</label>
        <label className="field grow2"><span>Bölüm başlığı</span><input value={ig.title} maxLength={80} onChange={(e) => set({ title: e.target.value })} /></label>
        <label className="field"><span>Gönderi sayısı</span><input type="number" min={1} max={24} value={ig.count} onChange={(e) => set({ count: Number(e.target.value) })} /></label>
      </div>

      <h3 className="sub-title">Instagram bağlantısı</h3>
      {status?.hasToken ? (
        <div className="alert ok">
          Bağlı: <strong>@{status.userName ?? '?'}</strong> · anahtar {status.tokenHint} · {dateTime(status.tokenUpdatedUtc)}
          {status.lastError && <div className="text-error">Son hata: {status.lastError}</div>}
          <div><button className="btn small danger" onClick={async () => { if (confirm('Instagram bağlantısı kaldırılsın mı?')) { await api('/api/v1/admin/instagram/token', { method: 'DELETE' }); void load() } }}>Bağlantıyı kaldır</button></div>
        </div>
      ) : (
        <p className="muted">
          Son gönderileri çekmek için Instagram işletme veya içerik üreticisi hesabının <strong>uzun ömürlü erişim anahtarı</strong> gerekir
          (Meta for Developers → uygulama → Instagram API → "Instagram ile giriş" → anahtar oluştur). Anahtar sunucuda ayrı gizli dosyada tutulur,
          sitede görünmez; 30 günden eski anahtar otomatik yenilenir. Bağlantı yoksa ve Sosyal Medya'da Instagram adresi varsa anasayfada "takip edin" kartı çıkar.
        </p>
      )}
      <div className="form-row">
        <label className="field grow2"><span>{status?.hasToken ? 'Yeni anahtar' : 'Erişim anahtarı'}</span>
          <input type="password" value={token} onChange={(e) => setToken(e.target.value)} autoComplete="off" placeholder="IGAA…" /></label>
        <div className="field end"><button className="btn primary" disabled={busy || token.trim().length < 20} onClick={saveToken}>{busy ? 'Doğrulanıyor…' : 'Doğrula ve kaydet'}</button></div>
      </div>
      {error && <div className="alert error">{error}</div>}

      {posts.length > 0 && (
        <>
          <h3 className="sub-title">Önizleme</h3>
          <div className="media-grid">
            {posts.map((p) => (
              <figure key={p.id}><img src={p.imageUrl} alt="" referrerPolicy="no-referrer" /><figcaption>{dateTime(p.timestamp)}</figcaption></figure>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function TickerTab({ content, patch }: TabProps) {
  const t = content.ticker
  const set = (p: Partial<SiteContent['ticker']>) => patch({ ticker: { ...t, ...p } })
  const setItem = (i: number, p: Partial<{ text: string; enabled: boolean }>) =>
    set({ items: t.items.map((x, idx) => (idx === i ? { ...x, ...p } : x)) })

  return (
    <div>
      <div className="form-row">
        <label className="mini check big"><input type="checkbox" checked={t.enabled} onChange={(e) => set({ enabled: e.target.checked })} /> Kayan yazı açık</label>
        <label className="mini check big"><input type="checkbox" checked={t.showPrices} onChange={(e) => set({ showPrices: e.target.checked })} /> Canlı fiyatları da göster</label>
        <label className="field">
          <span>Bir tur süresi (sn) — büyük değer daha yavaş</span>
          <input type="number" min={5} max={600} value={t.durationSeconds} onChange={(e) => set({ durationSeconds: Number(e.target.value) })} />
        </label>
      </div>
      <div className="list-edit">
        {t.items.map((item, i) => (
          <div key={i} className="list-row">
            <input type="checkbox" checked={item.enabled} onChange={(e) => setItem(i, { enabled: e.target.checked })} title="Yayında" />
            <input className="grow-input" value={item.text} maxLength={300} onChange={(e) => setItem(i, { text: e.target.value })} placeholder="Duyuru metni" />
            <button className="icon-btn small" disabled={i === 0} onClick={() => set({ items: swap(t.items, i, i - 1) })} title="Yukarı">↑</button>
            <button className="icon-btn small" disabled={i === t.items.length - 1} onClick={() => set({ items: swap(t.items, i, i + 1) })} title="Aşağı">↓</button>
            <button className="icon-btn small danger" onClick={() => set({ items: t.items.filter((_, idx) => idx !== i) })} title="Sil">✕</button>
          </div>
        ))}
      </div>
      <button className="btn" onClick={() => set({ items: [...t.items, { text: '', enabled: true }] })}>+ Yazı ekle</button>
    </div>
  )
}

function SlidesTab({ content, patch, media, reloadMedia }: TabProps & { media: MediaFile[]; reloadMedia: () => Promise<void> }) {
  const slides = [...content.slides].sort((a, b) => a.order - b.order)
  const setSlides = (list: Slide[]) => patch({ slides: list.map((s, i) => ({ ...s, order: i + 1 })) })
  const setSlide = (id: string, p: Partial<Slide>) => setSlides(slides.map((s) => (s.id === id ? { ...s, ...p } : s)))

  async function onUploaded(file: MediaFile) {
    await reloadMedia()
    setSlides([...slides, { id: Math.random().toString(36).slice(2, 10), imageUrl: file.url, title: '', subtitle: '', enabled: true, order: slides.length + 1 }])
  }

  return (
    <div>
      <p className="muted">Anasayfadaki slayt fotoğrafları. Önerilen boyut 1600×600, en fazla 5 MB (JPG, PNG, WebP).</p>
      <Uploader onUploaded={onUploaded} label="Fotoğraf yükle ve slayt ekle" />
      <div className="slide-list">
        {slides.map((s, i) => (
          <div key={s.id} className={`slide-edit ${s.enabled ? '' : 'hidden-card'}`}>
            <img src={s.imageUrl} alt="" />
            <div className="slide-fields">
              <input value={s.title} maxLength={120} placeholder="Başlık" onChange={(e) => setSlide(s.id, { title: e.target.value })} />
              <input value={s.subtitle ?? ''} maxLength={240} placeholder="Alt yazı" onChange={(e) => setSlide(s.id, { subtitle: e.target.value })} />
              <MediaPicker media={media} value={s.imageUrl} onChange={(url) => setSlide(s.id, { imageUrl: url })} />
              <div className="row-actions">
                <label className="mini check"><input type="checkbox" checked={s.enabled} onChange={(e) => setSlide(s.id, { enabled: e.target.checked })} /> Yayında</label>
                <span className="grow" />
                <button className="icon-btn small" disabled={i === 0} onClick={() => setSlides(swap(slides, i, i - 1))} title="Yukarı">↑</button>
                <button className="icon-btn small" disabled={i === slides.length - 1} onClick={() => setSlides(swap(slides, i, i + 1))} title="Aşağı">↓</button>
                <button className="icon-btn small danger" onClick={() => setSlides(slides.filter((x) => x.id !== s.id))} title="Slaytı kaldır">✕</button>
              </div>
            </div>
          </div>
        ))}
        {slides.length === 0 && <p className="muted">Henüz slayt yok. Slayt yoksa anasayfada dükkan adı gösterilir.</p>}
      </div>
      <MediaLibrary media={media} content={content} reload={reloadMedia} />
    </div>
  )
}

function AboutTab({ content, patch, media, reloadMedia }: TabProps & { media: MediaFile[]; reloadMedia: () => Promise<void> }) {
  const a = content.about
  const set = (p: Partial<SiteContent['about']>) => patch({ about: { ...a, ...p } })
  return (
    <div>
      <DemoBar onFill={() => set({ title: demoAbout.title, body: demoAbout.body })} />
      <label className="field"><span>Başlık</span><input value={a.title} onChange={(e) => set({ title: e.target.value })} /></label>
      <label className="field">
        <span>Metin (boş satır yeni paragraf)</span>
        <textarea rows={10} value={a.body} maxLength={10000} onChange={(e) => set({ body: e.target.value })} />
      </label>
      <div className="field">
        <span>Fotoğraf</span>
        <div className="row-actions wrap">
          {a.imageUrl && <img src={a.imageUrl} alt="" className="thumb" />}
          <MediaPicker media={media} value={a.imageUrl ?? ''} allowEmpty onChange={(url) => set({ imageUrl: url || null })} />
          <Uploader label="Yeni fotoğraf yükle" onUploaded={async (f) => { await reloadMedia(); set({ imageUrl: f.url }) }} />
        </div>
      </div>
    </div>
  )
}

type ContactText = Exclude<keyof SiteContent['contact'], 'locations'>

function ContactTab({ content, patch, media, reloadMedia }: TabProps & { media: MediaFile[]; reloadMedia: () => Promise<void> }) {
  const c = content.contact
  const set = (p: Partial<SiteContent['contact']>) => patch({ contact: { ...c, ...p } })
  const locations = c.locations ?? []
  const setLoc = (i: number, p: Partial<BranchLocation>) => set({ locations: locations.map((l, j) => (j === i ? { ...l, ...p } : l)) })
  const field = (key: ContactText, label: string, placeholder?: string) => (
    <label className="field">
      <span>{label}</span>
      <input value={c[key] ?? ''} placeholder={placeholder} onChange={(e) => set({ [key]: e.target.value || null })} />
    </label>
  )
  return (
    <div>
      <DemoBar onFill={() => set(demoContact)} />
      <div className="form-row">
        {field('phone', 'Telefon', '0 (212) 000 00 00')}
        {field('whatsApp', 'WhatsApp', '90 5xx xxx xx xx')}
        {field('email', 'E-posta', 'info@ornek.com')}
      </div>
      {field('address', 'Adres')}
      {field('workingHours', 'Çalışma saatleri')}

      <h3 className="sub-title">Sayfa görseli</h3>
      <div className="row-actions wrap">
        <div className="logo-preview wide"><img src={c.imageUrl || '/demo/contact.jpg'} alt="" /></div>
        <MediaPicker media={media} value={c.imageUrl ?? ''} allowEmpty onChange={(url) => set({ imageUrl: url || null })} />
        <Uploader label="Yeni görsel yükle" onUploaded={async (f) => { await reloadMedia(); set({ imageUrl: f.url }) }} />
      </div>
      <p className="muted small">Boş bırakılırsa demo görsel gösterilir. Önerilen boyut 1600×400.</p>

      <h3 className="sub-title">Şubeler ve haritalar ({locations.length}/10)</h3>
      <p className="muted small">
        Her şube haritasıyla Anasayfa, Hakkımızda ve İletişim'de gösterilir. "Harita araması" Google Haritalar'da aranacak metindir
        (ör. "Kapalıçarşı, Fatih, İstanbul") ya da "41.0107,28.9680" gibi koordinattır.
      </p>
      <div className="list-edit">
        {locations.map((l, i) => (
          <div key={i} className="branch-edit">
            <div className="form-row">
              <label className="field"><span>Şube adı</span><input value={l.name} maxLength={80} onChange={(e) => setLoc(i, { name: e.target.value })} /></label>
              <label className="field grow2"><span>Harita araması</span><input value={l.mapQuery} maxLength={200} onChange={(e) => setLoc(i, { mapQuery: e.target.value })} /></label>
            </div>
            <div className="form-row">
              <label className="field grow2"><span>Adres</span><input value={l.address ?? ''} maxLength={200} onChange={(e) => setLoc(i, { address: e.target.value || null })} /></label>
              <label className="field"><span>Telefon</span><input value={l.phone ?? ''} maxLength={30} onChange={(e) => setLoc(i, { phone: e.target.value || null })} /></label>
              <label className="field"><span>Çalışma saatleri</span><input value={l.workingHours ?? ''} maxLength={120} onChange={(e) => setLoc(i, { workingHours: e.target.value || null })} /></label>
            </div>
            <div className="row-actions wrap">
              {l.mapQuery.trim() && (
                <a className="btn small" href={`https://www.google.com/maps?q=${encodeURIComponent(l.mapQuery.trim())}`} target="_blank" rel="noopener noreferrer">Haritada kontrol et ↗</a>
              )}
              <span className="grow" />
              <button className="icon-btn small" disabled={i === 0} onClick={() => set({ locations: swap(locations, i, i - 1) })} title="Yukarı">↑</button>
              <button className="icon-btn small" disabled={i === locations.length - 1} onClick={() => set({ locations: swap(locations, i, i + 1) })} title="Aşağı">↓</button>
              <button className="btn small danger" onClick={() => set({ locations: locations.filter((_, j) => j !== i) })}>Şubeyi sil</button>
            </div>
          </div>
        ))}
      </div>
      <div className="row-actions wrap">
        <button className="btn" disabled={locations.length >= 10} onClick={() => set({ locations: [...locations, { name: 'Yeni şube', mapQuery: '' }] })}>+ Şube ekle</button>
        <button className="btn" onClick={() => { if (confirm('Kapalıçarşı ve Kuyumcukent demo şubeleri eklensin mi?')) set({ locations: [...locations, ...demoLocations].slice(0, 10) }) }}>Demo şubeleri ekle</button>
      </div>

      <h3 className="sub-title">Tek harita (eski alan, isteğe bağlı)</h3>
      {field('mapEmbedUrl', 'Google Haritalar yerleştirme adresi', 'https://www.google.com/maps/embed?pb=…')}
      <p className="muted small">Google Haritalar → Paylaş → Haritayı yerleştir → HTML'deki src="…" adresini yapıştırın. Şube haritaları için gerekmez.</p>
    </div>
  )
}

const demoLocations: BranchLocation[] = [
  { name: 'Kapalıçarşı Şubesi', address: 'Kapalıçarşı, Beyazıt / Fatih / İstanbul', mapQuery: 'Kapalıçarşı, Fatih, İstanbul', workingHours: 'Pazartesi - Cumartesi 09:00 - 19:00' },
  { name: 'Kuyumcukent Şubesi', address: 'Kuyumcukent, Yenibosna / Bahçelievler / İstanbul', mapQuery: 'Kuyumcukent, Bahçelievler, İstanbul', workingHours: 'Pazartesi - Cumartesi 09:00 - 18:30' },
]

function MobileTab({ content, patch }: TabProps) {
  const origin = window.location.origin
  const id = content.brokerId
  return (
    <div>
      <label className="field">
        <span>Aracı kimliği (brokerId) — küçük harf, rakam, tire</span>
        <input value={id} onChange={(e) => patch({ brokerId: e.target.value.toLowerCase() })} />
      </label>
      <p className="muted">Mobil uygulama bu kimlik ve <strong>Mobil / WebSocket</strong> sayfasında oluşturulan anahtarla veri çeker.
        Anahtar <code>X-Api-Key</code> başlığında gönderilir.</p>
      <table className="data-table">
        <thead><tr><th>Uç</th><th>Ne döner</th></tr></thead>
        <tbody>
          <tr><td className="mono">GET {origin}/api/v1/mobile/{id}</td><td>Aracı bilgisi + fiyatlar + içerik + soket adresi (açılışta tek istek)</td></tr>
          <tr><td className="mono">GET {origin}/api/v1/mobile/{id}/prices</td><td>Marjlı fiyatlar (istemci filtresiyle)</td></tr>
          <tr><td className="mono">GET {origin}/api/v1/mobile/{id}/content</td><td>Kayan yazı, slaytlar, hakkımızda, iletişim (fotoğraflar tam adres)</td></tr>
          <tr><td className="mono">WS {origin.replace(/^http/, 'ws')}/ws/prices?key=…</td><td>Canlı fiyat; içerik değişince <code>{'{"type":"content-updated"}'}</code></td></tr>
        </tbody>
      </table>
    </div>
  )
}

function Uploader({ label, onUploaded }: { label: string; onUploaded: (f: MediaFile) => void | Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <div className="uploader">
      <label className={`btn ${busy ? 'disabled' : ''}`}>
        {busy ? 'Yükleniyor…' : label}
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          hidden
          disabled={busy}
          onChange={async (e) => {
            const file = e.target.files?.[0]
            e.target.value = ''
            if (!file) return
            setBusy(true)
            setError(null)
            try {
              const body = new FormData()
              body.append('file', file)
              await onUploaded(await api<MediaFile>('/api/v1/admin/media', { method: 'POST', body }))
            } catch (err) {
              setError((err as Error).message)
            } finally {
              setBusy(false)
            }
          }}
        />
      </label>
      {error && <span className="text-error">{error}</span>}
    </div>
  )
}

function MediaPicker({ media, value, onChange, allowEmpty }: { media: MediaFile[]; value: string; onChange: (url: string) => void; allowEmpty?: boolean }) {
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)} aria-label="Fotoğraf seç">
      {allowEmpty && <option value="">Fotoğraf yok</option>}
      {!allowEmpty && !media.some((m) => m.url === value) && <option value={value}>{value}</option>}
      {media.map((m) => <option key={m.name} value={m.url}>{m.name}</option>)}
    </select>
  )
}

function MediaLibrary({ media, content, reload }: { media: MediaFile[]; content: SiteContent; reload: () => Promise<void> }) {
  const [error, setError] = useState<string | null>(null)
  const used = new Set([...content.slides.map((s) => s.imageUrl), content.about.imageUrl ?? '', content.branding.headerLogoUrl ?? '', content.branding.footerImageUrl ?? '', content.home.introImageUrl ?? '', content.contact.imageUrl ?? ''])
  if (media.length === 0) return null
  return (
    <details className="library">
      <summary>Yüklenen fotoğraflar ({media.length})</summary>
      {error && <div className="alert error">{error}</div>}
      <div className="media-grid">
        {media.map((m) => (
          <figure key={m.name}>
            <img src={m.url} alt={m.name} />
            <figcaption>
              {(m.size / 1024).toFixed(0)} KB
              {used.has(m.url) ? <span className="badge ok">Kullanılıyor</span> : (
                <button className="btn small danger" onClick={async () => {
                  if (!confirm('Fotoğraf kalıcı olarak silinsin mi?')) return
                  setError(null)
                  try { await api(`/api/v1/admin/media/${encodeURIComponent(m.name)}`, { method: 'DELETE' }); await reload() }
                  catch (e) { setError((e as Error).message) }
                }}>Sil</button>
              )}
            </figcaption>
          </figure>
        ))}
      </div>
    </details>
  )
}

// Demo metinler: yayına almadan önce gerçek bilgilerle değiştirilmeli.
const demoHome: HomeSection = {
  introTitle: 'Altın ve dövizde güvenilir adres',
  introText: 'Yılların tecrübesiyle altın ve döviz alım satımında şeffaf fiyat, hızlı işlem ve güler yüzlü hizmet sunuyoruz. Fiyatlarımız piyasayla anlık güncellenir; dilediğiniz fiyatı asistanımızdan sabitleyip dükkanımızda işleminizi tamamlayabilirsiniz.',
  features: [
    { icon: '⚡', title: 'Anlık fiyat', text: 'Altın ve döviz fiyatları piyasayla saniyeler içinde güncellenir.' },
    { icon: '🔒', title: 'Kur sabitleme', text: 'Asistan üzerinden fiyatınızı belirli bir süre için sabitleyin, referans numaranızla gelin.' },
    { icon: '🛡', title: 'Güvenli işlem', text: 'Faturalı, kayıtlı ve şeffaf alım satım.' },
    { icon: '🤝', title: 'Uzman kadro', text: 'Ziynet, gram ve yatırım altınında doğru yönlendirme.' },
  ],
  stats: [
    { value: '25+', label: 'Yıllık tecrübe' },
    { value: '10.000+', label: 'Mutlu müşteri' },
    { value: '7/24', label: 'Canlı fiyat' },
  ],
  ctaTitle: 'Fiyat sabitlemek ister misiniz?',
  ctaText: 'Sağ alttaki asistanı açın, ürünü seçin; fiyatınız dakikalarca sabit kalsın.',
}

const demoAbout = {
  title: 'Hakkımızda',
  body: [
    'DEMO, altın ve döviz alım satımında yılların verdiği tecrübeyle müşterilerine güvenilir ve şeffaf hizmet sunar.',
    'Gram altından ziynet altınlarına, dolar ve euro başta olmak üzere tüm majör dövizlere kadar geniş bir ürün yelpazesinde, piyasaya en yakın fiyatları anlık olarak paylaşıyoruz.',
    'Sitemizdeki asistan üzerinden fiyatınızı belirli bir süre için sabitleyebilir, verilen referans numarasıyla dükkanımızda işleminizi hızlıca tamamlayabilirsiniz.',
    'Misyonumuz; doğru fiyat, hızlı işlem ve güler yüzlü hizmet.',
  ].join('\n\n'),
}

const demoContact: Omit<SiteContent['contact'], 'locations'> = {
  phone: '0 (212) 000 00 00',
  whatsApp: '90 500 000 00 00',
  email: 'info@ornek.com',
  address: 'Örnek Mah. Kuyumcular Cad. No: 1, Fatih / İstanbul',
  workingHours: 'Pazartesi - Cumartesi 09:00 - 19:00, Pazar kapalı',
  // Harita adresi demo ile doldurulmaz: Google Haritalar'dan dükkanın kendi yerleştirme adresi alınmalı.
  mapEmbedUrl: null,
}

function DemoBar({ onFill }: { onFill: () => void }) {
  return (
    <div className="demo-bar">
      <span>Hızlı başlamak için örnek metin doldurabilirsiniz; yayına almadan önce gerçek bilgilerle değiştirin.</span>
      <span className="grow" />
      <button className="btn small" onClick={() => { if (confirm('Bu sekmedeki alanlar demo içerikle doldurulsun mu? (Kaydet\'e basana kadar yayına çıkmaz.)')) onFill() }}>
        Demo içerik doldur
      </button>
    </div>
  )
}

function swap<T>(list: T[], a: number, b: number): T[] {
  const copy = [...list]
  ;[copy[a], copy[b]] = [copy[b], copy[a]]
  return copy
}
