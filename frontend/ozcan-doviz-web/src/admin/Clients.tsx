import { useEffect, useState, type FormEvent } from 'react'
import { api, json, type ClientCreated, type ClientView } from '../api'
import { dateTime } from '../format'

/**
 * Mobil uygulama / dış site için WebSocket istemcileri. Her istemcinin kendi anahtarı ve isteğe bağlı ürün filtresi vardır.
 * Anahtar yalnızca oluşturulduğunda gösterilir; sunucu özetini saklar.
 */
export function Clients() {
  const [clients, setClients] = useState<ClientView[]>([])
  const [name, setName] = useState('')
  const [codes, setCodes] = useState('')
  const [created, setCreated] = useState<ClientCreated | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = () => api<ClientView[]>('/api/v1/admin/clients').then(setClients)
  useEffect(() => { void load() }, [])

  async function create(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      const r = await api<ClientCreated>('/api/v1/admin/clients', { method: 'POST', body: json({ name, allowedCodes: codes || null, isActive: true }) })
      setCreated(r)
      setName('')
      setCodes('')
      void load()
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function toggle(c: ClientView) {
    await api(`/api/v1/admin/clients/${c.id}`, { method: 'PUT', body: json({ name: c.name, allowedCodes: c.allowedCodes, isActive: !c.isActive }) })
    void load()
  }

  async function rotate(c: ClientView) {
    if (!confirm(`${c.name} için yeni anahtar üretilsin mi? Eski anahtar hemen geçersiz olur.`)) return
    setCreated(await api<ClientCreated>(`/api/v1/admin/clients/${c.id}/rotate`, { method: 'POST' }))
    void load()
  }

  async function remove(c: ClientView) {
    if (!confirm(`${c.name} silinsin mi?`)) return
    await api(`/api/v1/admin/clients/${c.id}`, { method: 'DELETE' })
    void load()
  }

  return (
    <div className="page">
      <section className="panel form-panel">
        <div className="form-title">Mobil / WebSocket İstemcisi Oluştur</div>
        <form className="pad" onSubmit={create}>
          <p className="muted">
            Müşterinin mobil uygulaması veya başka bir site, marjlı fiyatları canlı almak için bir istemci anahtarı kullanır.
            Ham sağlayıcı fiyatı hiçbir zaman gönderilmez.
          </p>
          <div className="form-row">
            <label className="field">
              <span>İstemci adı</span>
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder="ör. DEMO iOS" required maxLength={100} />
            </label>
            <label className="field grow2">
              <span>İzinli ürün kodları (boş = tümü)</span>
              <input value={codes} onChange={(e) => setCodes(e.target.value)} placeholder="USD, EUR, HAS, CEYREK_YENI" />
            </label>
          </div>
          {error && <div className="alert error">{error}</div>}
          <button className="btn primary">Anahtar oluştur</button>
        </form>
      </section>

      {created && (
        <section className="panel">
          <div className="form-title">Anahtar hazır: {created.client.name}</div>
          <div className="pad">
            <div className="alert warn">Bu anahtar bir daha gösterilmez. Şimdi kopyalayın.</div>
            <CopyLine label="API anahtarı" value={created.apiKey} />
            <CopyLine label="WebSocket adresi" value={created.webSocketUrl} />
            <CopyLine label="REST adresi" value={created.restUrl} />
            <details>
              <summary>Örnek kod (JavaScript / React Native)</summary>
              <pre className="code">{`const ws = new WebSocket('${created.webSocketUrl}')
ws.onmessage = (e) => {
  const msg = JSON.parse(e.data)
  if (msg.type === 'prices') {
    // msg.items: [{ code, name, category, buy, sell, decimals, direction }]
    render(msg.items)
  }
}
// Bağlantıyı canlı tutmak için isteğe bağlı:
setInterval(() => ws.readyState === 1 && ws.send('ping'), 25000)`}</pre>
            </details>
            <button className="btn" onClick={() => setCreated(null)}>Kapat</button>
          </div>
        </section>
      )}

      <section className="panel">
        <div className="form-title">İstemciler</div>
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr><th>Ad</th><th>Anahtar</th><th>İzinli ürünler</th><th>Durum</th><th>Son bağlantı</th><th /></tr>
            </thead>
            <tbody>
              {clients.map((c) => (
                <tr key={c.id}>
                  <td><strong>{c.name}</strong></td>
                  <td className="mono">{c.keyPrefix}…</td>
                  <td>{c.allowedCodes || <span className="muted">Tümü</span>}</td>
                  <td><span className={`badge ${c.isActive ? 'ok' : ''}`}>{c.isActive ? 'Aktif' : 'Pasif'}</span></td>
                  <td>{dateTime(c.lastSeenUtc)}</td>
                  <td className="actions">
                    <button className="btn small" onClick={() => toggle(c)}>{c.isActive ? 'Pasif yap' : 'Aktif yap'}</button>
                    <button className="btn small" onClick={() => rotate(c)}>Anahtar yenile</button>
                    <button className="btn small danger" onClick={() => remove(c)}>Sil</button>
                  </td>
                </tr>
              ))}
              {clients.length === 0 && <tr><td colSpan={6} className="empty">Henüz istemci yok.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

function CopyLine({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false)
  return (
    <div className="copy-line">
      <span className="copy-label">{label}</span>
      <code>{value}</code>
      <button className="btn small" onClick={async () => {
        try { await navigator.clipboard.writeText(value); setCopied(true); setTimeout(() => setCopied(false), 1500) } catch { /* izin yok */ }
      }}>{copied ? 'Kopyalandı' : 'Kopyala'}</button>
    </div>
  )
}
