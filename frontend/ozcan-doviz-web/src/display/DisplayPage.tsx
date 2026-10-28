import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { useParams } from 'react-router-dom'
import type { DisplayScreen, PublicPriceItem } from '../api'
import { initials, money } from '../format'
import { useLive } from '../useLivePrices'
import { Ticker } from '../site/Ticker'

/** Ekran tanımını okur; panelde değişince 30 sn içinde sayfayı yenilemeden uygular. */
export function useDisplay(id: string | undefined) {
  const [screen, setScreen] = useState<DisplayScreen | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!id) return
    let alive = true
    const load = () =>
      fetch(`/api/v1/displays/${encodeURIComponent(id)}`, { cache: 'no-store' })
        .then(async (r) => {
          if (!alive) return
          if (r.ok) { setScreen(await r.json()); setError(null) }
          else setError(r.status === 404 ? 'Ekran bulunamadı veya kapalı.' : `Sunucu ${r.status} döndü.`)
        })
        .catch(() => alive && setError('Sunucuya ulaşılamadı; yeniden deneniyor…'))
    void load()
    const t = setInterval(load, 30000)
    return () => { alive = false; clearInterval(t) }
  }, [id])
  return { screen, error }
}

export function pick(screen: DisplayScreen, items: PublicPriceItem[]) {
  const codes = screen.layout.productCodes
  if (codes.length === 0) return items
  const by = new Map(items.map((i) => [i.code.toUpperCase(), i]))
  return codes.map((c) => by.get(c.toUpperCase())).filter((i): i is PublicPriceItem => i !== undefined)
}

/**
 * HDMI ile bağlı TV / pano için tam ekran fiyat sayfası: /ekran/{id}.
 * Fare gizlenir; "Tam ekran" düğmesi ilk tıklamada kaybolur. Fiyatlar SignalR ile canlı gelir.
 */
export function DisplayPage() {
  const { id } = useParams()
  const { screen, error } = useDisplay(id)
  const { message, content, state } = useLive()
  const [page, setPage] = useState(0)
  const [clock, setClock] = useState(() => new Date())
  const [full, setFull] = useState(false)

  const rows = useMemo(() => (screen ? pick(screen, message?.items ?? []) : []), [screen, message])
  const size = screen?.layout.pageSize || rows.length || 1
  const pages = Math.max(1, Math.ceil(rows.length / size))

  useEffect(() => {
    const t = setInterval(() => setClock(new Date()), 1000)
    return () => clearInterval(t)
  }, [])
  useEffect(() => {
    if (!screen || pages < 2) { setPage(0); return }
    const t = setInterval(() => setPage((p) => (p + 1) % pages), screen.layout.pageSeconds * 1000)
    return () => clearInterval(t)
  }, [screen, pages])
  useEffect(() => {
    const onChange = () => setFull(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', onChange)
    return () => document.removeEventListener('fullscreenchange', onChange)
  }, [])
  useEffect(() => { if (screen) document.title = screen.name }, [screen])

  if (!screen) return <div className="display dark center-msg">{error ?? 'Ekran yükleniyor…'}</div>

  const l = screen.layout
  const visible = rows.slice(page * size, page * size + size)
  const perColumn = Math.ceil(visible.length / l.columns)
  const columns = Array.from({ length: l.columns }, (_, c) => visible.slice(c * perColumn, (c + 1) * perColumn)).filter((c) => c.length > 0)

  return (
    <div className={`display ${l.theme} ${l.orientation}`} style={{ '--scale': l.fontScale } as CSSProperties}>
      <header className="display-head">
        <div className="display-brand">
          {l.showLogo && (content?.branding.headerLogoUrl
            ? <img src={content.branding.headerLogoUrl} alt="" />
            : <span className="brand-mark">{initials(content?.shopName ?? screen.name)}</span>)}
          <span className="display-title">{l.title || content?.shopName || screen.name}</span>
        </div>
        {l.showClock && (
          <div className="display-clock">
            <span className="mono">{clock.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}</span>
            <small>{clock.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', weekday: 'long' })}</small>
          </div>
        )}
      </header>

      <main className="display-body" style={{ gridTemplateColumns: `repeat(${columns.length || 1}, minmax(0, 1fr))`, '--rows': Math.max(perColumn, 6), '--cols': columns.length || 1 } as CSSProperties}>
        {columns.map((col, ci) => (
          <table key={ci} className="display-table">
            <colgroup><col className="c-name" /><col /><col /></colgroup>
            <thead>
              <tr>
                <th />
                {l.showBuy && <th>ALIŞ</th>}
                {l.showSell && <th>SATIŞ</th>}
              </tr>
            </thead>
            <tbody>
              {col.map((i) => (
                <tr key={i.code}>
                  <td className="d-name">{i.name}</td>
                  {l.showBuy && <td className="d-num mono">{money(i.buy, i.decimals)}</td>}
                  {l.showSell && <td className={`d-num mono ${i.direction}`}>{money(i.sell, i.decimals)}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        ))}
        {rows.length === 0 && <div className="center-msg">{message ? 'Seçili ürünlerin fiyatı yok.' : 'Fiyatlar yükleniyor…'}</div>}
      </main>

      <footer className="display-foot">
        {pages > 1 && <span className="display-pages">{page + 1} / {pages}</span>}
        {state !== 'live' && <span className="display-offline">Bağlantı bekleniyor…</span>}
        {l.showTicker && <Ticker />}
      </footer>

      {!full && (
        <button className="display-full" onClick={() => void document.documentElement.requestFullscreen?.().catch(() => {})}>
          ⛶ Tam ekran
        </button>
      )}
    </div>
  )
}
