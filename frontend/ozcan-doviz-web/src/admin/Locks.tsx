import { useEffect, useMemo, useState } from 'react'
import { api, json, type AdminLock, type RateLockSettings } from '../api'
import { dateTime, money } from '../format'

const statusLabels: Record<AdminLock['status'], string> = {
  Active: 'Geçerli',
  Expired: 'Süresi doldu',
  Completed: 'Tamamlandı',
  Cancelled: 'İptal',
}

/** Mesaj botundan gelen kur sabitlemeleri (App_Data/locks.json) ve sabitleme ayarları. */
export function Locks() {
  const [settings, setSettings] = useState<RateLockSettings | null>(null)
  const [locks, setLocks] = useState<AdminLock[]>([])
  const [filter, setFilter] = useState<'all' | AdminLock['status']>('all')
  const [search, setSearch] = useState('')
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const [, setTick] = useState(0)

  const load = () => api<AdminLock[]>('/api/v1/admin/locks?take=500').then(setLocks)
  useEffect(() => {
    void api<RateLockSettings>('/api/v1/admin/locks/settings').then(setSettings)
    void load()
    const t = setInterval(load, 5000)
    const clock = setInterval(() => setTick((x) => x + 1), 1000)
    return () => { clearInterval(t); clearInterval(clock) }
  }, [])

  const rows = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('tr-TR')
    return locks.filter((l) =>
      (filter === 'all' || l.status === filter)
      && (!q || l.lock.ref.toLowerCase().includes(q) || l.lock.customerPhone.includes(q)
          || l.lock.customerName.toLocaleLowerCase('tr-TR').includes(q)))
  }, [locks, filter, search])

  const counts = useMemo(() => {
    const c: Record<string, number> = { Active: 0, Expired: 0, Completed: 0, Cancelled: 0 }
    for (const l of locks) c[l.status]++
    return c
  }, [locks])

  async function saveSettings() {
    if (!settings) return
    setMessage(null)
    try {
      setSettings(await api<RateLockSettings>('/api/v1/admin/locks/settings', { method: 'PUT', body: json(settings) }))
      setMessage({ ok: true, text: 'Kaydedildi. Bot yeni süreyi hemen kullanır.' })
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message })
    }
  }

  async function close(ref: string, action: 'complete' | 'cancel') {
    const label = action === 'complete' ? 'tamamlandı' : 'iptal'
    if (!confirm(`${ref} ${label} olarak işaretlensin mi?`)) return
    try {
      await api(`/api/v1/admin/locks/${encodeURIComponent(ref)}/${action}`, { method: 'POST' })
      void load()
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message })
    }
  }

  return (
    <div className="page">
      <div className="stats">
        <Stat label="Geçerli" value={counts.Active} tone="ok" />
        <Stat label="Süresi doldu" value={counts.Expired} tone="muted" />
        <Stat label="Tamamlandı" value={counts.Completed} tone="gold" />
        <Stat label="İptal" value={counts.Cancelled} tone="danger" />
      </div>

      <section className="panel form-panel">
        <div className="form-title">Kur Sabitleme Ayarları</div>
        {settings && (
          <div className="pad">
            <div className="form-row">
              <label className="mini check big">
                <input type="checkbox" checked={settings.enabled} onChange={(e) => setSettings({ ...settings, enabled: e.target.checked })} /> Botta kur sabitleme açık
              </label>
              <label className="field">
                <span>Sabitleme süresi (dakika)</span>
                <input type="number" min={1} max={240} value={settings.minutes} onChange={(e) => setSettings({ ...settings, minutes: Number(e.target.value) })} />
              </label>
              <label className="field">
                <span>Tek seferde en fazla miktar</span>
                <input type="number" min={1} value={settings.maxQuantity} onChange={(e) => setSettings({ ...settings, maxQuantity: Number(e.target.value) })} />
              </label>
              <div className="field end"><button className="btn primary" onClick={saveSettings}>Kaydet</button></div>
            </div>
            {message && <div className={`alert ${message.ok ? 'ok' : 'error'}`}>{message.text}</div>}
          </div>
        )}
      </section>

      <section className="panel">
        <div className="form-title">Sabitlemeler</div>
        <div className="toolbar">
          <select value={filter} onChange={(e) => setFilter(e.target.value as typeof filter)} aria-label="Durum">
            <option value="all">Tümü ({locks.length})</option>
            {Object.entries(statusLabels).map(([k, v]) => <option key={k} value={k}>{v} ({counts[k]})</option>)}
          </select>
          <input className="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ref no, ad veya telefon ara" />
        </div>
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Ref no</th><th>Durum</th><th>Ürün</th><th>Yön</th>
                <th className="num">Birim</th><th className="num">Miktar</th><th className="num">Toplam</th>
                <th>Müşteri</th><th>Oluşturma</th><th>Bitiş</th><th />
              </tr>
            </thead>
            <tbody>
              {rows.map(({ lock: l, status }) => {
                const left = Math.max(0, Math.floor((new Date(l.expiresUtc).getTime() - Date.now()) / 1000))
                return (
                  <tr key={l.ref}>
                    <td className="mono"><strong>{l.ref}</strong></td>
                    <td>
                      <span className={`status-pill ${status.toLowerCase()}`}>{statusLabels[status]}</span>
                      {status === 'Active' && <div className="muted small mono">{Math.floor(left / 60)}:{String(left % 60).padStart(2, '0')}</div>}
                    </td>
                    <td>{l.productName}</td>
                    <td>{l.side === 'Buy' ? 'Müşteri alıyor' : 'Müşteri satıyor'}</td>
                    <td className="num mono">{money(l.unitPrice, l.decimals)}</td>
                    <td className="num mono">{l.quantity}</td>
                    <td className="num mono"><strong>{money(l.total, 2)}</strong></td>
                    <td>{l.customerName}<div className="muted small"><a href={`tel:${l.customerPhone}`}>{l.customerPhone}</a></div></td>
                    <td>{dateTime(l.createdUtc)}</td>
                    <td>{l.closedUtc ? <>{dateTime(l.closedUtc)}<div className="muted small">{l.closedBy}</div></> : dateTime(l.expiresUtc)}</td>
                    <td className="actions">
                      {!l.closedStatus && (
                        <>
                          <button className="btn small primary" onClick={() => close(l.ref, 'complete')}>Tamamlandı</button>
                          <button className="btn small danger" onClick={() => close(l.ref, 'cancel')}>İptal</button>
                        </>
                      )}
                    </td>
                  </tr>
                )
              })}
              {rows.length === 0 && <tr><td colSpan={11} className="empty">Kayıt yok.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

function Stat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className={`stat ${tone}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </div>
  )
}
