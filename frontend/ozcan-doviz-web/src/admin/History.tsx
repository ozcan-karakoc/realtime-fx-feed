import { useState, type FormEvent } from 'react'
import { api, type HaremRate } from '../api'
import { money } from '../format'

const today = () => new Date().toISOString().slice(0, 10)

/** Harem Altın geçmiş kurları. Kayıt tarihi Harem'in döndürdüğü zamandır (istek gününden önce olabilir). */
export function History() {
  const [date, setDate] = useState(today)
  const [code, setCode] = useState('')
  const [rows, setRows] = useState<HaremRate[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function search(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const q = new URLSearchParams({ date })
      if (code.trim()) q.set('code', code.trim())
      setRows(await api<HaremRate[]>(`/api/v1/rates/harem/history?${q}`))
    } catch (err) {
      setRows(null)
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="page">
      <section className="panel form-panel">
        <div className="form-title">Harem Altın Geçmiş Kurlar</div>
        <form className="pad" onSubmit={search}>
          <div className="form-row">
            <label className="field">
              <span>Tarih</span>
              <input type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} required />
            </label>
            <label className="field">
              <span>Kod (boş = tümü)</span>
              <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="ALTIN, CEYREK_YENI, AYAR22" />
            </label>
            <div className="field end">
              <button className="btn primary" disabled={busy}>{busy ? 'Getiriliyor…' : 'Getir'}</button>
            </div>
          </div>
          {error && <div className="alert error">{error}</div>}
        </form>
      </section>

      {rows && (
        <section className="panel">
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr><th>Kod</th><th className="num">Alış</th><th className="num">Satış</th><th className="num">Yüksek</th><th className="num">Düşük</th><th>Kayıt tarihi</th></tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.code}>
                    <td className="mono"><strong>{r.code}</strong></td>
                    <td className="num mono buy">{money(r.buy, 4)}</td>
                    <td className="num mono sell">{money(r.sell, 4)}</td>
                    <td className="num mono">{money(r.high, 4)}</td>
                    <td className="num mono">{money(r.low, 4)}</td>
                    {/* Harem'in yerel saatidir; saat dilimi dönüşümü yapılmaz. */}
                    <td className="mono">{r.recordDate.replace('T', ' ')}</td>
                  </tr>
                ))}
                {rows.length === 0 && <tr><td colSpan={6} className="empty">Kayıt yok.</td></tr>}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  )
}
