import { useEffect, useState } from 'react'
import { Link, Navigate, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { auth } from '../api'
import { initials } from '../format'
import { ThemeToggle } from '../theme'

const groups = [
  {
    title: 'Genel',
    links: [
      { to: '/admin', label: 'Yönetim Paneli', icon: '▦', end: true },
      { to: '/admin/kur-sabitleme', label: 'Kur Sabitlemeleri', icon: '⏱' },
      { to: '/admin/bildirimler', label: 'Bildirim Paneli', icon: '✉' },
    ],
  },
  {
    title: 'Fiyat',
    links: [
      { to: '/admin/fiyat-guncelleme', label: 'Fiyat Güncelleme', icon: '％' },
      { to: '/admin/saglayicilar', label: 'Fiyat Sağlayıcıları', icon: '⇄' },
      { to: '/admin/gecmis-kurlar', label: 'Geçmiş Kurlar', icon: '↺' },
    ],
  },
  {
    title: 'Site ve Mobil',
    links: [
      { to: '/admin/site-icerigi', label: 'Site İçeriği', icon: '▣' },
      { to: '/admin/ekranlar', label: 'TV / Pano / LED', icon: '▭' },
      { to: '/admin/mobil-soket', label: 'Mobil / WebSocket', icon: '⌁' },
    ],
  },
  {
    title: 'Sistem',
    links: [
      { to: '/admin/kullanicilar', label: 'Kullanıcı Yönetimi', icon: '☺' },
      { to: '/admin/islem-izi', label: 'İşlem İzi', icon: '☰' },
    ],
  },
]

const allLinks = groups.flatMap((g) => g.links)

export function AdminLayout() {
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(() => window.innerWidth >= 800)
  const [shopName, setShopName] = useState('DEMO')
  useEffect(() => { if (window.innerWidth < 800) setOpen(false) }, [location.pathname])
  // Menüdeki ad, sağlayıcı ayarlarındaki dükkan adından gelir (sayfa değişince tazelenir).
  useEffect(() => {
    fetch('/api/v1/content', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((c) => c?.shopName && setShopName(c.shopName))
      .catch(() => {})
  }, [location.pathname])
  if (!auth.token) return <Navigate to="/admin/login" replace />

  const current = allLinks.find((l) => (l.end ? location.pathname === l.to : location.pathname.startsWith(l.to)))

  function logout() {
    auth.clear()
    navigate('/admin/login', { replace: true })
  }

  return (
    <div className={`admin ${open ? '' : 'collapsed'}`}>
      <nav className="admin-side" aria-label="Yönetim menüsü">
        <Link to="/admin" className="side-brand">
          <span className="brand-mark small">{initials(shopName)}</span>
          <span>
            <strong>{shopName}</strong>
            <small>Yönetim</small>
          </span>
        </Link>
        {groups.map((g) => (
          <div key={g.title} className="side-group">
            <div className="side-group-title">{g.title}</div>
            {g.links.map((l) => (
              <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `side-link ${isActive ? 'active' : ''}`}>
                <span className="side-icon" aria-hidden>{l.icon}</span>
                <span>{l.label}</span>
              </NavLink>
            ))}
          </div>
        ))}
        <a href="/" target="_blank" rel="noopener" className="side-link site-link">
          <span className="side-icon" aria-hidden>↗</span>
          <span>Siteyi görüntüle</span>
        </a>
      </nav>

      <header className="admin-top">
        <button className="icon-btn" onClick={() => setOpen((v) => !v)} aria-label="Menüyü aç/kapat">☰</button>
        <h1 className="admin-title">{current?.label ?? 'Yönetim Paneli'}</h1>
        <span className="grow" />
        <ThemeToggle className="admin-theme" />
        <span className="admin-user"><span className="avatar">{(auth.user ?? '?').slice(0, 1).toUpperCase()}</span>{auth.user}</span>
        <button className="btn small" onClick={logout}>Çıkış yap</button>
      </header>

      <main className="admin-main">
        <Outlet />
      </main>
      <footer className="admin-foot">© {new Date().getFullYear()} {shopName} · Yönetim Paneli</footer>
      {open && <div className="side-backdrop" onClick={() => setOpen(false)} />}
    </div>
  )
}
