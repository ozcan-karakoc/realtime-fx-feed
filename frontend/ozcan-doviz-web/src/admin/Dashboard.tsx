import { useEffect, useState } from 'react'
import { api, type BoardItem } from '../api'
import { categoryLabels, dateTime, money, time } from '../format'

interface Live {
  provider: string
  providerName: string
  updatedUtc: string
  error?: string | null
  items: BoardItem[]
  rawCount: number
  mobileConnections: { clientId: number; clientName: string; connectedUtc: string }[]
}

/** Kaynak ve marjlı fiyat yan yana; 2 sn'de bir yenilenir. */
export function Dashboard() {
  const [live, setLive] = useState<Live | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    const load = () =>
      api<Live>('/api/v1/admin/live')
        .then((l) => { if (alive) { setLive(l); setError(null) } })
        .catch((e) => alive && setError((e as Error).message))
    void load()
    const t = setInterval(load, 2000)
    return () => { alive = false; clearInterval(t) }
  }, [])

  return (
    <div className="page">
      <div className="stats">
        <Stat label="Etkin sağlayıcı" value={live?.providerName || '—'} />
        <Stat label="Son güncelleme" value={time(live?.updatedUtc)} />
        <Stat label="Yayındaki ürün" value={String(live?.items.length ?? 0)} />
        <Stat label="Ham sembol" value={String(live?.rawCount ?? 0)} />
        <Stat label="Bağlı mobil soket" value={String(live?.mobileConnections.length ?? 0)} />
      </div>

      {error && <div className="alert error">{error}</div>}
      {live?.error && <div className="alert warn">{live.error}</div>}

      <section className="panel">
        <div className="form-title">Canlı Fiyatlar (kaynak → marjlı)</div>
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Ürün</th><th>Kategori</th><th>Kaynak</th>
                <th className="num">Kaynak alış</th><th className="num">Kaynak satış</th>
                <th className="num">Alış</th><th className="num">Satış</th><th className="num">Fark (satış)</th>
              </tr>
            </thead>
            <tbody>
              {live?.items.map((i) => (
                <tr key={i.code}>
                  <td><strong>{i.name}</strong> <span className="muted">{i.code}</span></td>
                  <td>{categoryLabels[i.category] ?? i.category}</td>
                  <td className="mono">{i.sourceSymbol}</td>
                  <td className="num mono">{money(i.sourceBuy, i.decimals)}</td>
                  <td className="num mono">{money(i.sourceSell, i.decimals)}</td>
                  <td className="num mono buy">{money(i.buy, i.decimals)}</td>
                  <td className="num mono sell">{money(i.sell, i.decimals)}</td>
                  <td className="num mono">{money(i.sell - i.sourceSell, i.decimals)}</td>
                </tr>
              ))}
              {live && live.items.length === 0 && <tr><td colSpan={8} className="empty">Henüz fiyat yok.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {live && live.mobileConnections.length > 0 && (
        <section className="panel">
          <div className="form-title">Bağlı Mobil İstemciler</div>
          <ul className="plain-list">
            {live.mobileConnections.map((c, idx) => (
              <li key={idx}>{c.clientName} <span className="muted">· {dateTime(c.connectedUtc)}</span></li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="stat">
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </div>
  )
}
