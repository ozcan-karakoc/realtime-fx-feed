import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { PublicNotice } from '../api'
import { useLive } from '../useLivePrices'

const DISMISSED = 'ozcan.notices.dismissed'

function loadDismissed(): string[] {
  try { return JSON.parse(sessionStorage.getItem(DISMISSED) ?? '[]') as string[] } catch { return [] }
}

function dismiss(id: string) {
  try { sessionStorage.setItem(DISMISSED, JSON.stringify([...loadDismissed(), id].slice(-50))) } catch { /* gizli pencere */ }
}

const icons: Record<PublicNotice['level'], string> = { info: 'ℹ', success: '✓', warning: '!', danger: '⚠' }

/**
 * Panelden gönderilen bildirimler: sayfa açıkken anında üstte belirir. Süreli bildirimleri siteyi sonradan
 * açanlar da görür. Kapatılan bildirim bu sekmede bir daha gösterilmez; panelde geri çekilen hemen kaybolur.
 */
export function Notices() {
  const { onNotice } = useLive()
  const [notices, setNotices] = useState<PublicNotice[]>([])

  useEffect(() => {
    const hidden = new Set(loadDismissed())
    fetch('/api/v1/notices/active', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : []))
      .then((list: PublicNotice[]) => setNotices((cur) => merge(cur, list.filter((n) => !hidden.has(n.id)))))
      .catch(() => {})
    return onNotice((e) => {
      if (e.type === 'notice') setNotices((cur) => merge(cur, [e.notice]))
      else setNotices((cur) => cur.filter((n) => n.id !== e.id))
    })
  }, [onNotice])

  if (notices.length === 0) return null

  const close = (id: string) => {
    dismiss(id)
    setNotices((cur) => cur.filter((n) => n.id !== id))
  }

  return (
    <div className="notices" role="region" aria-label="Duyurular" aria-live="polite">
      {notices.map((n) => (
        <div key={n.id} className={`notice ${n.level}`}>
          <span className="notice-icon" aria-hidden>{icons[n.level]}</span>
          <div className="notice-body">
            <strong>{n.title}</strong>
            {n.message && <p>{n.message}</p>}
            {n.linkUrl && (n.linkUrl.startsWith('/')
              ? <Link to={n.linkUrl} className="notice-link" onClick={() => close(n.id)}>{n.linkText || 'İncele'} →</Link>
              : <a href={n.linkUrl} className="notice-link" target="_blank" rel="noopener noreferrer">{n.linkText || 'İncele'} →</a>)}
          </div>
          <button className="notice-close" onClick={() => close(n.id)} aria-label="Kapat">✕</button>
        </div>
      ))}
    </div>
  )
}

function merge(current: PublicNotice[], incoming: PublicNotice[]) {
  const ids = new Set(current.map((n) => n.id))
  return [...incoming.filter((n) => !ids.has(n.id)), ...current].slice(0, 3)
}
