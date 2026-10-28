import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { initials } from '../format'
import { ThemeToggle } from '../theme'
import { useLive } from '../useLivePrices'
import { ChatBot } from './ChatBot'
import { FloatingSocial, SocialRow } from './FloatingSocial'
import { Ticker } from './Ticker'

const nav = [
  { to: '/', label: 'Anasayfa', end: true },
  { to: '/canli-piyasalar', label: 'Canlı Piyasalar' },
  { to: '/hakkimizda', label: 'Hakkımızda' },
  { to: '/iletisim', label: 'İletişim' },
]

export function SiteLayout() {
  const { content, state, message } = useLive()
  const [menu, setMenu] = useState(false)
  const [clock, setClock] = useState(() => new Date())
  const location = useLocation()
  const shopName = content?.shopName ?? 'DEMO'

  useEffect(() => {
    const t = setInterval(() => setClock(new Date()), 1000)
    return () => clearInterval(t)
  }, [])
  useEffect(() => { setMenu(false); window.scrollTo(0, 0) }, [location.pathname])
  useEffect(() => { document.title = `${shopName} | Canlı Altın ve Döviz` }, [shopName])

  const status = state === 'live' ? (message?.stale ? 'Gecikmeli' : 'Canlı')
    : state === 'reconnecting' ? 'Yeniden bağlanıyor' : state === 'offline' ? 'Bağlantı yok' : 'Bağlanıyor'

  return (
    <div className="site">
      <header className="site-head">
        <Link to="/" className="brand">
          {content?.branding.headerLogoUrl ? (
            <img src={content.branding.headerLogoUrl} alt={shopName} className="brand-logo" />
          ) : (
            <>
              <span className="brand-mark">{initials(shopName)}</span>
              <span>
                <span className="brand-name">{shopName}</span>
                <span className="brand-sub">Altın &amp; Döviz</span>
              </span>
            </>
          )}
        </Link>
        <ThemeToggle className="mobile-only" />
        <button className="menu-btn" onClick={() => setMenu((v) => !v)} aria-label="Menü" aria-expanded={menu}>☰</button>
        <nav className={`site-nav ${menu ? 'open' : ''}`}>
          {nav.map((n) => (
            <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'active' : '')}>{n.label}</NavLink>
          ))}
        </nav>
        <div className="board-meta">
          <span className={`live-dot ${state}`} />
          <span>{status}</span>
          <span className="sep">·</span>
          <span className="mono">{clock.toLocaleTimeString('tr-TR')}</span>
          <ThemeToggle />
        </div>
      </header>

      <Ticker />

      <main className="site-main">
        <Outlet />
      </main>

      <footer className="site-foot">
        <div className="foot-cols">
          <div>
            {content?.branding.footerImageUrl
              ? <img src={content.branding.footerImageUrl} alt={shopName} className="foot-image" />
              : <div className="foot-title">{shopName}</div>}
            <p className="foot-text">Fiyatlar bilgi amaçlıdır; işlem öncesi teyit ediniz.</p>
            <SocialRow />
          </div>
          <div>
            <div className="foot-title">Sayfalar</div>
            {nav.map((n) => <Link key={n.to} to={n.to} className="foot-link">{n.label}</Link>)}
          </div>
          <div>
            <div className="foot-title">İletişim</div>
            {content?.contact.phone && <a className="foot-link" href={`tel:${content.contact.phone.replace(/\s/g, '')}`}>{content.contact.phone}</a>}
            {content?.contact.email && <a className="foot-link" href={`mailto:${content.contact.email}`}>{content.contact.email}</a>}
            {content?.contact.address && <span className="foot-text">{content.contact.address}</span>}
          </div>
        </div>
        <div className="foot-bottom">
          <span>© {new Date().getFullYear()} {shopName}</span>
          <Link to="/admin" className="foot-link inline">Yönetim</Link>
        </div>
      </footer>
      <FloatingSocial />
      <ChatBot />
    </div>
  )
}
