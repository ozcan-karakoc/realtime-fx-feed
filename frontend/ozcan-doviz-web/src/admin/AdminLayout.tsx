import { useState } from 'react'
import { Navigate, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { auth } from '../api'

const links = [
  { to: '/admin', label: 'Yönetim Paneli', icon: '▦', end: true },
  { to: '/', label: 'Ana Sayfa', icon: '⌂', end: true },
  { to: '/admin/fiyat-guncelleme', label: 'Fiyat Güncelleme', icon: '⚙' },
  { to: '/admin/saglayicilar', label: 'Fiyat Sağlayıcıları', icon: '⇄' },
  { to: '/admin/mobil-soket', label: 'Mobil / WebSocket', icon: '📱' },
  { to: '/admin/gecmis-kurlar', label: 'Geçmiş Kurlar', icon: '🕘' },
  { to: '/admin/kullanicilar', label: 'Kullanıcı Yönetimi', icon: '👥' },
  { to: '/admin/islem-izi', label: 'İşlem İzi', icon: '☰' },
]

export function AdminLayout() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(true)
  if (!auth.token) return <Navigate to="/admin/login" replace />

  function logout() {
    auth.clear()
    navigate('/admin/login', { replace: true })
  }

  return (
    <div className={`admin ${open ? '' : 'collapsed'}`}>
      <header className="admin-top">
        <button className="icon-btn" onClick={() => setOpen((v) => !v)} aria-label="Menüyü aç/kapat">☰</button>
        <span className="admin-title">Yönetim Paneli</span>
        <span className="grow" />
        <span className="admin-user">{auth.user}</span>
        <button className="link-btn" onClick={logout}>⎋ ÇIKIŞ YAP</button>
      </header>
      <nav className="admin-side">
        {links.map((l) => (
          <NavLink key={l.to} to={l.to} end={l.end} className={({ isActive }) => `side-link ${isActive ? 'active' : ''}`}
            onClick={() => window.innerWidth < 800 && setOpen(false)}>
            <span className="side-icon" aria-hidden>{l.icon}</span>
            <span>{l.label}</span>
          </NavLink>
        ))}
      </nav>
      <main className="admin-main">
        <Outlet />
      </main>
      <footer className="admin-foot">© {new Date().getFullYear()} Özcan Döviz. Tüm hakları saklıdır.</footer>
    </div>
  )
}
