import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import './styles.css'
import { applyInitialTheme } from './theme'
import { LiveProvider } from './useLivePrices'
import { SiteLayout } from './site/SiteLayout'
import { Home } from './site/Home'
import { Markets } from './site/Markets'
import { About } from './site/About'
import { Contact } from './site/Contact'
import { Login } from './admin/Login'
import { AdminLayout } from './admin/AdminLayout'
import { Dashboard } from './admin/Dashboard'
import { Margins } from './admin/Margins'
import { Providers } from './admin/Providers'
import { SiteContentPage } from './admin/SiteContent'
import { Locks } from './admin/Locks'
import { Displays } from './admin/Displays'
import { DisplayPage } from './display/DisplayPage'
import { SenderPage } from './display/SenderPage'
import { Clients } from './admin/Clients'
import { History } from './admin/History'
import { Users } from './admin/Users'
import { Audit } from './admin/Audit'

applyInitialTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<LiveProvider><SiteLayout /></LiveProvider>}>
          <Route index element={<Home />} />
          <Route path="canli-piyasalar" element={<Markets />} />
          <Route path="hakkimizda" element={<About />} />
          <Route path="iletisim" element={<Contact />} />
        </Route>
        <Route path="/ekran/:id" element={<LiveProvider><DisplayPage /></LiveProvider>} />
        <Route path="/ekran/:id/gonder" element={<LiveProvider><SenderPage /></LiveProvider>} />
        <Route path="/admin/login" element={<Login />} />
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<Dashboard />} />
          <Route path="fiyat-guncelleme" element={<Margins />} />
          <Route path="saglayicilar" element={<Providers />} />
          <Route path="site-icerigi" element={<SiteContentPage />} />
          <Route path="kur-sabitleme" element={<Locks />} />
          <Route path="ekranlar" element={<Displays />} />
          <Route path="mobil-soket" element={<Clients />} />
          <Route path="gecmis-kurlar" element={<History />} />
          <Route path="kullanicilar" element={<Users />} />
          <Route path="islem-izi" element={<Audit />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
