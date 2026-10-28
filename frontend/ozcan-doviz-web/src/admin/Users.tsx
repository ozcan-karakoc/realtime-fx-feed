import { useEffect, useState, type FormEvent } from 'react'
import { api, auth, json, type UserView } from '../api'
import { dateTime } from '../format'

export function Users() {
  const [users, setUsers] = useState<UserView[]>([])
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  const load = () => api<UserView[]>('/api/v1/admin/users').then(setUsers)
  useEffect(() => { void load() }, [])

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setMessage(null)
    try {
      await fn()
      setMessage({ ok: true, text: ok })
      void load()
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message })
    }
  }

  function create(e: FormEvent) {
    e.preventDefault()
    void run(async () => {
      await api('/api/v1/admin/users', { method: 'POST', body: json({ userName, password }) })
      setUserName('')
      setPassword('')
    }, 'Kullanıcı eklendi.')
  }

  function changeOwn(e: FormEvent) {
    e.preventDefault()
    void run(async () => {
      await api('/api/v1/auth/change-password', { method: 'POST', body: json({ currentPassword: current, newPassword: next }) })
      setCurrent('')
      setNext('')
    }, 'Şifreniz değişti.')
  }

  function reset(u: UserView) {
    const pw = prompt(`${u.userName} için yeni şifre (en az 8 karakter):`)
    if (!pw) return
    void run(() => api(`/api/v1/admin/users/${u.id}/password`, { method: 'PUT', body: json({ password: pw }) }), 'Şifre sıfırlandı.')
  }

  return (
    <div className="page">
      {message && <div className={`alert ${message.ok ? 'ok' : 'error'}`}>{message.text}</div>}

      <section className="panel">
        <div className="form-title">Kullanıcı Yönetimi</div>
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th>Kullanıcı</th><th>Rol</th><th>Durum</th><th>Son giriş</th><th /></tr></thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.id}>
                  <td><strong>{u.userName}</strong>{u.userName === auth.user && <span className="muted"> (siz)</span>}</td>
                  <td>{u.role}</td>
                  <td><span className={`badge ${u.isActive ? 'ok' : ''}`}>{u.isActive ? 'Aktif' : 'Pasif'}</span></td>
                  <td>{dateTime(u.lastLoginUtc)}</td>
                  <td className="actions">
                    <button className="btn small" onClick={() => reset(u)}>Şifre sıfırla</button>
                    <button className="btn small" onClick={() => run(() => api(`/api/v1/admin/users/${u.id}/active?value=${!u.isActive}`, { method: 'PUT' }), 'Güncellendi.')}>
                      {u.isActive ? 'Pasif yap' : 'Aktif yap'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="two-col">
        <section className="panel form-panel">
          <div className="form-title">Yeni Yönetici</div>
          <form className="pad" onSubmit={create}>
            <label className="field"><span>Kullanıcı adı</span><input value={userName} onChange={(e) => setUserName(e.target.value)} required /></label>
            <label className="field"><span>Şifre (en az 8)</span><input type="password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required autoComplete="new-password" /></label>
            <button className="btn primary">Ekle</button>
          </form>
        </section>
        <section className="panel form-panel">
          <div className="form-title">Şifremi Değiştir</div>
          <form className="pad" onSubmit={changeOwn}>
            <label className="field"><span>Mevcut şifre</span><input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} required autoComplete="current-password" /></label>
            <label className="field"><span>Yeni şifre (en az 8)</span><input type="password" minLength={8} value={next} onChange={(e) => setNext(e.target.value)} required autoComplete="new-password" /></label>
            <button className="btn primary">Değiştir</button>
          </form>
        </section>
      </div>
    </div>
  )
}
