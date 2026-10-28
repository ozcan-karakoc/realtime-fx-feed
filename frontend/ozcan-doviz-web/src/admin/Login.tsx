import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError, auth, json } from '../api'

export function Login() {
  const navigate = useNavigate()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const res = await api<{ token: string; userName: string }>('/api/v1/auth/login', {
        method: 'POST',
        body: json({ userName, password }),
      })
      auth.set(res.token, res.userName)
      navigate('/admin', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 429 ? 'Çok fazla deneme. Bir dakika sonra tekrar deneyin.' : (err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={submit}>
        <div className="form-title">Yönetim Paneli Girişi</div>
        <div className="login-body">
          <label className="field">
            <span>Kullanıcı adı</span>
            <input value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" required autoFocus />
          </label>
          <label className="field">
            <span>Şifre</span>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
          </label>
          {error && <div className="alert error">{error}</div>}
          <button className="btn primary block" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</button>
        </div>
      </form>
    </div>
  )
}
