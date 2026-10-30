import { useEffect, useState } from 'react'
import { initials, time } from '../format'
import { useLive } from '../useLivePrices'
import { FULLSCREEN_EVENT, MarketBoards } from './Markets'

/**
 * Canlı piyasa tablolarının tam ekran görünümü. Header'daki düğme olay gönderir; tarayıcının tam ekran modu
 * aynı tıklamada istenir (tarayıcılar yalnızca kullanıcı hareketinde izin verir). Esc veya ✕ ile kapanır.
 */
export function FullscreenMarkets() {
  const { content, message, state } = useLive()
  const [open, setOpen] = useState(false)
  const [clock, setClock] = useState(() => new Date())

  useEffect(() => {
    const show = () => {
      setOpen(true)
      document.documentElement.requestFullscreen?.().catch(() => { /* izin yoksa sayfa içinde tam ekran kalır */ })
    }
    const onChange = () => { if (!document.fullscreenElement) setOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener(FULLSCREEN_EVENT, show)
    document.addEventListener('fullscreenchange', onChange)
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener(FULLSCREEN_EVENT, show)
      document.removeEventListener('fullscreenchange', onChange)
      window.removeEventListener('keydown', onKey)
    }
  }, [])

  useEffect(() => {
    if (!open) return
    const t = setInterval(() => setClock(new Date()), 1000)
    document.body.style.overflow = 'hidden'
    return () => { clearInterval(t); document.body.style.overflow = '' }
  }, [open])

  if (!open) return null

  const close = () => {
    setOpen(false)
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => {})
  }
  const shopName = content?.shopName ?? 'DEMO'

  return (
    <div className="fs-markets" role="dialog" aria-label="Canlı piyasalar tam ekran">
      <header className="fs-head">
        <div className="brand">
          {content?.branding.headerLogoUrl
            ? <img src={content.branding.headerLogoUrl} alt={shopName} className="brand-logo" />
            : <><span className="brand-mark">{initials(shopName)}</span><span className="brand-name">{shopName}</span></>}
        </div>
        <div className="fs-title">Canlı Piyasalar</div>
        <div className="fs-meta">
          <span className={`live-dot ${state}`} />
          <span className="mono fs-clock">{clock.toLocaleTimeString('tr-TR')}</span>
          <button className="fs-close" onClick={close} aria-label="Tam ekrandan çık">✕</button>
        </div>
      </header>
      <div className="fs-body">
        <MarketBoards big />
      </div>
      <footer className="fs-foot muted-light">Son güncelleme: {time(message?.updatedUtc)} · Çıkmak için Esc</footer>
    </div>
  )
}
