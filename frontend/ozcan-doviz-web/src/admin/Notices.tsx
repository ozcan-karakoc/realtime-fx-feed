import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, json, type SiteNotice } from '../api'
import { dateTime } from '../format'

const levels: { value: SiteNotice['level']; label: string }[] = [
  { value: 'info', label: 'Bilgi' },
  { value: 'success', label: 'Kampanya / olumlu' },
  { value: 'warning', label: 'Uyarı' },
  { value: 'danger', label: 'Önemli' },
]

const durations = [
  { value: 0, label: 'Yalnız şu an açık olanlar' },
  { value: 30, label: '30 dakika' },
  { value: 60, label: '1 saat' },
  { value: 240, label: '4 saat' },
  { value: 1440, label: '24 saat' },
]

const empty = { title: '', message: '', level: 'info' as SiteNotice['level'], linkUrl: '', linkText: '', displayMinutes: 60 }

/**
 * Bildirim Paneli: siteye anında duyuru. Gönderildiği an sitesi açık olan herkesin ekranına düşer;
 * süre seçildiyse o süre içinde siteyi açanlar da görür. Geri çekilen bildirim açık ekranlardan kalkar.
 */
export function NoticesAdmin() {
  const [form, setForm] = useState(empty)
  const [list, setList] = useState<SiteNotice[]>([])
  const [online, setOnline] = useState(0)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string; details?: string[] } | null>(null)

  const load = () => api<{ online: number; notices: SiteNotice[] }>('/api/v1/admin/notices').then((r) => { setList(r.notices); setOnline(r.online) })
  useEffect(() => {
    void load()
    const t = setInterval(load, 10000)
    return () => clearInterval(t)
  }, [])

  async function send(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      await api('/api/v1/admin/notices', { method: 'POST', body: json(form) })
      setMessage({ ok: true, text: `Gönderildi: şu an açık ${online} ekrana anında iletildi.` })
      setForm(empty)
      void load()
    } catch (err) {
      const e2 = err as ApiError
      setMessage({ ok: false, text: e2.message, details: e2.details })
    } finally {
      setBusy(false)
    }
  }

  async function withdraw(n: SiteNotice) {
    if (!confirm(`"${n.title}" geri çekilsin mi? Açık ekranlardan da kaldırılır.`)) return
    await api(`/api/v1/admin/notices/${n.id}`, { method: 'DELETE' })
    void load()
  }

  const now = Date.now()
  const active = (n: SiteNotice) => !n.withdrawnUtc && n.displayMinutes > 0 && now < new Date(n.createdUtc.endsWith('Z') ? n.createdUtc : n.createdUtc + 'Z').getTime() + n.displayMinutes * 60000

  return (
    <div className="page">
      <div className="stats">
        <div className="stat ok"><div className="stat-label">Şu an açık ekran</div><div className="stat-value">{online}</div></div>
        <div className="stat gold"><div className="stat-label">Yayındaki bildirim</div><div className="stat-value">{list.filter(active).length}</div></div>
        <div className="stat"><div className="stat-label">Toplam gönderilen</div><div className="stat-value">{list.length}</div></div>
      </div>

      <div className="two-col notice-admin">
        <section className="panel form-panel">
          <div className="form-title">Yeni bildirim</div>
          <form className="pad" onSubmit={send}>
            <label className="field"><span>Başlık</span>
              <input value={form.title} maxLength={80} required onChange={(e) => setForm({ ...form, title: e.target.value })} placeholder="ör. Bugün 19:00'a kadar açığız" /></label>
            <label className="field"><span>Mesaj (isteğe bağlı)</span>
              <textarea rows={3} maxLength={500} value={form.message} onChange={(e) => setForm({ ...form, message: e.target.value })} /></label>
            <div className="form-row">
              <label className="field"><span>Tür</span>
                <select value={form.level} onChange={(e) => setForm({ ...form, level: e.target.value as SiteNotice['level'] })}>
                  {levels.map((l) => <option key={l.value} value={l.value}>{l.label}</option>)}
                </select></label>
              <label className="field"><span>Kimler görsün</span>
                <select value={form.displayMinutes} onChange={(e) => setForm({ ...form, displayMinutes: Number(e.target.value) })}>
                  {durations.map((d) => <option key={d.value} value={d.value}>{d.value === 0 ? d.label : `Açık olanlar + ${d.label} boyunca açanlar`}</option>)}
                </select></label>
            </div>
            <div className="form-row">
              <label className="field grow2"><span>Bağlantı (isteğe bağlı)</span>
                <input value={form.linkUrl} onChange={(e) => setForm({ ...form, linkUrl: e.target.value })} placeholder="/canli-piyasalar veya https://…" /></label>
              <label className="field"><span>Bağlantı metni</span>
                <input value={form.linkText} maxLength={40} onChange={(e) => setForm({ ...form, linkText: e.target.value })} placeholder="İncele" /></label>
            </div>
            {message && (
              <div className={`alert ${message.ok ? 'ok' : 'error'}`}>
                {message.text}
                {message.details && message.details.length > 0 && <ul>{message.details.map((d) => <li key={d}>{d}</li>)}</ul>}
              </div>
            )}
            <button className="btn primary" disabled={busy || !form.title.trim()}>{busy ? 'Gönderiliyor…' : `Gönder (${online} açık ekran)`}</button>
          </form>
        </section>

        <section className="panel">
          <div className="form-title">Önizleme</div>
          <div className="pad notice-preview">
            <div className={`notice ${form.level}`}>
              <span className="notice-icon" aria-hidden>{form.level === 'info' ? 'ℹ' : form.level === 'success' ? '✓' : form.level === 'warning' ? '!' : '⚠'}</span>
              <div className="notice-body">
                <strong>{form.title || 'Başlık'}</strong>
                {form.message && <p>{form.message}</p>}
                {form.linkUrl && <span className="notice-link">{form.linkText || 'İncele'} →</span>}
              </div>
              <span className="notice-close">✕</span>
            </div>
            <p className="muted small">Sitede sağ üstte belirir; ziyaretçi kapatabilir.</p>
          </div>
        </section>
      </div>

      <section className="panel">
        <div className="form-title">Gönderilenler</div>
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th>Zaman</th><th>Başlık</th><th>Tür</th><th>Süre</th><th>Durum</th><th>Gönderen</th><th /></tr></thead>
            <tbody>
              {list.map((n) => (
                <tr key={n.id}>
                  <td>{dateTime(n.createdUtc)}</td>
                  <td><strong>{n.title}</strong>{n.message && <div className="muted small">{n.message}</div>}</td>
                  <td>{levels.find((l) => l.value === n.level)?.label}</td>
                  <td>{n.displayMinutes ? `${n.displayMinutes} dk` : 'Anlık'}</td>
                  <td>
                    {n.withdrawnUtc ? <span className="status-pill cancelled">Geri çekildi</span>
                      : active(n) ? <span className="status-pill active">Yayında</span>
                      : <span className="status-pill expired">Gönderildi</span>}
                  </td>
                  <td>{n.createdBy}</td>
                  <td className="actions">{!n.withdrawnUtc && <button className="btn small danger" onClick={() => withdraw(n)}>Geri çek</button>}</td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={7} className="empty">Henüz bildirim gönderilmedi.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
