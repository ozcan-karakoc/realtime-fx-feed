import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import './styles.css'
import { PublicBoard } from './pages/PublicBoard'
import { Login } from './admin/Login'
import { AdminLayout } from './admin/AdminLayout'
import { Dashboard } from './admin/Dashboard'
import { Margins } from './admin/Margins'
import { Providers } from './admin/Providers'
import { Clients } from './admin/Clients'
import { History } from './admin/History'
import { Users } from './admin/Users'
import { Audit } from './admin/Audit'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<PublicBoard />} />
        <Route path="/admin/login" element={<Login />} />
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<Dashboard />} />
          <Route path="fiyat-guncelleme" element={<Margins />} />
          <Route path="saglayicilar" element={<Providers />} />
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
