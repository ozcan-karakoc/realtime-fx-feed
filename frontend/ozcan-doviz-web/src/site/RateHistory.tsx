import { useEffect, useMemo, useRef, useState } from 'react'
import type { HistoryCode, HistorySeries } from '../api'
import { money } from '../format'
import { useLive } from '../useLivePrices'

const dateFmt = new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' })
const longDate = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' })
const parse = (d: string) => new Date(d + 'T00:00:00')

/** Geçmiş Kurlar: sembol + aralık seçimi, alış/satış grafiği, özet; altında hesaplayıcı. */
export function RateHistory() {
  const { content } = useLive()
  const pages = content?.pages
  const [codes, setCodes] = useState<HistoryCode[]>([])
  const [daysOptions, setDaysOptions] = useState<number[]>([7, 30, 90])
  const [code, setCode] = useState('ALTIN')
  const [days, setDays] = useState(30)
  const [series, setSeries] = useState<HistorySeries | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    fetch('/api/v1/history/codes')
      .then((r) => (r.ok ? r.json() : null))
      .then((b) => { if (b) { setCodes(b.codes); setDaysOptions(b.days) } })
      .catch(() => {})
  }, [])

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    fetch(`/api/v1/history/series?code=${encodeURIComponent(code)}&days=${days}`)
      .then(async (r) => {
        if (!alive) return
        if (!r.ok) { setError(r.status === 404 ? 'Bu sayfa şu an kapalı.' : 'Veri alınamadı.'); setSeries(null); return }
        setSeries(await r.json())
      })
      .catch(() => alive && setError('Bağlantı hatası.'))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [code, days])

  const stats = useMemo(() => {
    const p = series?.points ?? []
    if (p.length === 0) return null
    const first = p[0]
    const last = p[p.length - 1]
    const sells = p.map((x) => x.sell)
    return {
      first, last,
      change: last.sell - first.sell,
      pct: first.sell ? ((last.sell - first.sell) / first.sell) * 100 : 0,
      high: Math.max(...sells),
      low: Math.min(...sells),
    }
  }, [series])

  if (pages && !pages.history) return <div className="section muted-light">Bu sayfa şu an kapalı.</div>

  const groups: { key: HistoryCode['group']; title: string }[] = [{ key: 'altin', title: 'Altın' }, { key: 'doviz', title: 'Döviz' }]
  const dec = series?.decimals ?? 2

  return (
    <div className="section history-page">
      <div className="section-head">
        <h2>{pages?.historyTitle ?? 'Geçmiş Kurlar'}</h2>
        <div className="seg on-site">
          {daysOptions.map((d) => (
            <button key={d} className={d === days ? 'active' : ''} onClick={() => setDays(d)}>{d === 7 ? '1 hafta' : d === 30 ? '1 ay' : '3 ay'}</button>
          ))}
        </div>
      </div>

      <div className="history-layout">
        <aside className="history-codes">
          {groups.map((g) => (
            <div key={g.key}>
              <div className="codes-title">{g.title}</div>
              {codes.filter((c) => c.group === g.key).map((c) => (
                <button key={c.code} className={`code-chip ${c.code === code ? 'active' : ''}`} onClick={() => setCode(c.code)}>{c.name}</button>
              ))}
            </div>
          ))}
        </aside>

        <div className="history-main">
          <div className="chart-card">
            <div className="chart-head">
              <div>
                <h3>{series?.name ?? '…'}</h3>
                <span className="muted-light small">Günlük kapanış · piyasa verisi (Harem Altın){loading && series ? " · güncelleniyor…" : ""}</span>
              </div>
              {stats && (
                <div className={`chart-change ${stats.change >= 0 ? 'up' : 'down'}`}>
                  <span className="mono">{money(stats.last.sell, dec)}</span>
                  <small className="mono">{stats.change >= 0 ? '▲' : '▼'} {money(Math.abs(stats.change), dec)} ({stats.pct >= 0 ? '+' : ''}{stats.pct.toFixed(2)}%)</small>
                </div>
              )}
            </div>
            {error && <div className="muted-light chart-empty">{error}</div>}
            {!error && loading && !series && <div className="muted-light chart-empty">Grafik hazırlanıyor… (ilk açılışta günler tek tek alınır)</div>}
            {!error && series && series.points.length < 2 && !loading && <div className="muted-light chart-empty">Bu aralıkta yeterli veri yok.</div>}
            {series && series.points.length >= 2 && <LineChart series={series} dimmed={loading} />}
            {stats && (
              <div className="chart-stats">
                <Stat label="Başlangıç" value={money(stats.first.sell, dec)} sub={longDate.format(parse(stats.first.date))} />
                <Stat label="Son" value={money(stats.last.sell, dec)} sub={longDate.format(parse(stats.last.date))} />
                <Stat label="En yüksek" value={money(stats.high, dec)} />
                <Stat label="En düşük" value={money(stats.low, dec)} />
              </div>
            )}
          </div>

          {(pages?.calculator ?? true) && <Calculator series={series} />}
        </div>
      </div>
    </div>
  )
}

function Stat({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="chart-stat">
      <span className="muted-light small">{label}</span>
      <strong className="mono">{value}</strong>
      {sub && <span className="muted-light small">{sub}</span>}
    </div>
  )
}

/** Alış ve satış çizgisi; fare/dokunmayla gün seçilir. Kitaplıksız SVG. */
function LineChart({ series, dimmed }: { series: HistorySeries; dimmed: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(800)
  const [hover, setHover] = useState<number | null>(null)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver(() => setWidth(el.clientWidth))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const height = width < 520 ? 220 : 300
  const pad = { l: 8, r: 8, t: 14, b: 26 }
  const pts = series.points
  const values = pts.flatMap((p) => [p.buy, p.sell])
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min || 1
  const x = (i: number) => pad.l + (i / (pts.length - 1)) * (width - pad.l - pad.r)
  const y = (v: number) => pad.t + (1 - (v - min) / span) * (height - pad.t - pad.b)
  const line = (k: 'buy' | 'sell') => pts.map((p, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(p[k]).toFixed(1)}`).join(' ')
  const area = `${line('sell')} L${x(pts.length - 1)},${height - pad.b} L${x(0)},${height - pad.b} Z`
  const ticks = [0, Math.floor((pts.length - 1) / 2), pts.length - 1]

  const pick = (clientX: number) => {
    const rect = ref.current!.getBoundingClientRect()
    const i = Math.round(((clientX - rect.left - pad.l) / (width - pad.l - pad.r)) * (pts.length - 1))
    setHover(Math.max(0, Math.min(pts.length - 1, i)))
  }
  const h = hover !== null ? pts[hover] : null

  return (
    <div ref={ref} className={`chart ${dimmed ? 'dimmed' : ''}`}
      onMouseMove={(e) => pick(e.clientX)} onMouseLeave={() => setHover(null)}
      onTouchMove={(e) => pick(e.touches[0].clientX)} onTouchEnd={() => setHover(null)}>
      <svg width={width} height={height} role="img" aria-label={`${series.name} alış ve satış grafiği`}>
        <defs>
          <linearGradient id="sellFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="var(--gold)" stopOpacity=".28" />
            <stop offset="1" stopColor="var(--gold)" stopOpacity="0" />
          </linearGradient>
        </defs>
        {[0.25, 0.5, 0.75].map((f) => (
          <line key={f} x1={pad.l} x2={width - pad.r} y1={pad.t + f * (height - pad.t - pad.b)} y2={pad.t + f * (height - pad.t - pad.b)} className="grid" />
        ))}
        <path d={area} fill="url(#sellFill)" />
        <path d={line('buy')} className="line buy" />
        <path d={line('sell')} className="line sell" />
        {ticks.map((i) => (
          <text key={i} x={x(i)} y={height - 6} textAnchor={i === 0 ? 'start' : i === pts.length - 1 ? 'end' : 'middle'} className="tick">
            {dateFmt.format(parse(pts[i].date))}
          </text>
        ))}
        {h && (
          <>
            <line x1={x(hover!)} x2={x(hover!)} y1={pad.t} y2={height - pad.b} className="cursor" />
            <circle cx={x(hover!)} cy={y(h.sell)} r={4.5} className="dot sell" />
            <circle cx={x(hover!)} cy={y(h.buy)} r={4.5} className="dot buy" />
          </>
        )}
      </svg>
      {h && (
        <div className="chart-tip" style={{ left: Math.min(Math.max(x(hover!) - 80, 0), width - 170) }}>
          <strong>{longDate.format(parse(h.date))}</strong>
          <span>Alış <b className="mono">{money(h.buy, series.decimals)}</b></span>
          <span>Satış <b className="mono">{money(h.sell, series.decimals)}</b></span>
        </div>
      )}
      <div className="chart-legend"><span className="lg buy">Alış</span><span className="lg sell">Satış</span></div>
    </div>
  )
}

/**
 * Hesaplayıcı: dükkanın güncel (marjlı) fiyatıyla birim ↔ TL; seçili aralığın başında alınsaydı bugünkü değeri.
 * Müşteri alıyorsa satış fiyatı, satıyorsa alış fiyatı kullanılır.
 */
function Calculator({ series }: { series: HistorySeries | null }) {
  const { message } = useLive()
  const items = message?.items ?? []
  const [code, setCode] = useState('')
  const [side, setSide] = useState<'buy' | 'sell'>('buy')
  const [mode, setMode] = useState<'units' | 'tl'>('units')
  const [amount, setAmount] = useState('1')

  useEffect(() => { if (!code && items.length) setCode(items.find((i) => i.code === 'GRAM')?.code ?? items[0].code) }, [items, code])
  const item = items.find((i) => i.code === code)
  // "1.250,5" (Türkçe) ve "1250.5" ikisi de okunur.
  const n = Number(amount.includes(',') ? amount.replace(/\./g, '').replace(',', '.') : amount)
  const valid = Number.isFinite(n) && n > 0
  const price = item ? (side === 'buy' ? item.sell : item.buy) : 0
  const result = valid && item ? (mode === 'units' ? n * price : n / price) : null

  // Seçili grafik aralığı: başındaki satış fiyatıyla alınan miktarın bugünkü alış fiyatıyla değeri.
  const back = useMemo(() => {
    const p = series?.points ?? []
    if (p.length < 2 || !valid) return null
    const invested = mode === 'tl' ? n : null
    if (invested === null) return null
    const units = invested / p[0].sell
    const now = units * p[p.length - 1].buy
    return { units, now, diff: now - invested, from: p[0].date }
  }, [series, n, valid, mode])

  return (
    <div className="calc-card">
      <h3>Hesaplayıcı</h3>
      <div className="calc-grid">
        <label>Ürün
          <select value={code} onChange={(e) => setCode(e.target.value)}>
            {items.map((i) => <option key={i.code} value={i.code}>{i.name}</option>)}
          </select>
        </label>
        <label>İşlem
          <div className="seg on-site">
            <button className={side === 'buy' ? 'active' : ''} onClick={() => setSide('buy')}>Almak istiyorum</button>
            <button className={side === 'sell' ? 'active' : ''} onClick={() => setSide('sell')}>Satmak istiyorum</button>
          </div>
        </label>
        <label>{mode === 'units' ? 'Miktar (adet / gram / birim)' : 'Tutar (TL)'}
          <div className="calc-input">
            <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
            <button onClick={() => setMode(mode === 'units' ? 'tl' : 'units')} title="Birim ↔ TL">{mode === 'units' ? 'birim → TL' : 'TL → birim'}</button>
          </div>
        </label>
      </div>
      <div className="calc-result">
        {item && result !== null ? (
          <>
            <span className="muted-light">{mode === 'units' ? `${amount} × ${item.name}` : `${amount} TL ile ${item.name}`}</span>
            <strong className="mono">{mode === 'units' ? `${money(result, 2)} ₺` : `${money(result, item.decimals > 2 ? 4 : 3)} birim`}</strong>
            <span className="muted-light small">Birim fiyat {money(price, item.decimals)} ₺ ({side === 'buy' ? 'satış' : 'alış'} fiyatımız, canlı)</span>
          </>
        ) : <span className="muted-light">Miktar girin.</span>}
      </div>
      {back && series && (
        <div className={`calc-back ${back.diff >= 0 ? 'up' : 'down'}`}>
          {longDate.format(parse(back.from))} tarihinde {amount} TL ile {series.name} alınsaydı ({money(back.units, 3)} birim),
          bugün piyasa değeri <b className="mono">{money(back.now, 2)} ₺</b> olurdu ({back.diff >= 0 ? '+' : ''}{money(back.diff, 2)} ₺).
        </div>
      )}
      <p className="muted-light small">Hesaplamalar bilgi amaçlıdır; işlem anındaki fiyat esas alınır.</p>
    </div>
  )
}
