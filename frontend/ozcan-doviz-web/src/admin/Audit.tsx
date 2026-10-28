import { useEffect, useState } from 'react'
import { api, type AuditEntry } from '../api'
import { dateTime } from '../format'

export function Audit() {
  const [rows, setRows] = useState<AuditEntry[]>([])
  useEffect(() => { void api<AuditEntry[]>('/api/v1/admin/audit?take=200').then(setRows) }, [])

  return (
    <div className="page">
      <section className="panel">
        <div className="form-title">İşlem İzi</div>
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th>Zaman</th><th>Kullanıcı</th><th>İşlem</th><th>Ayrıntı</th></tr></thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>{dateTime(r.timeUtc)}</td>
                  <td>{r.user}</td>
                  <td className="mono">{r.action}</td>
                  <td>{r.detail}</td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={4} className="empty">Kayıt yok.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
