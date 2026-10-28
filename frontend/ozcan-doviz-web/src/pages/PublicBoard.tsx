import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { PublicPriceItem } from '../api'
import { categoryLabels, money, time } from '../format'
import { useLivePrices } from '../useLivePrices'

const groups: { title: string; categories: string[] }[] = [
  { title: 'Altın Fiyatları', categories: ['altin'] },
  { title: 'Döviz Kurları', categories: ['doviz', 'parite', 'maden'] },
]

/** Herkese açık fiyat ekranı. Değişen satır kısa süre yeşil/kırmızı yanar. */
export function PublicBoard() {
  const { message, state } = useLivePrices()
  const [shopName, setShopName] = useState('Özcan Döviz')
  const [clock, setClock] = useState(() => new Date())
  const flashes = useFlashes(message?.items ?? [])

  useEffect(() => {
    fetch('/api/v1/prices')
      .then((r) => (r.ok ? r.json() : null))
      .then((b) => b?.shopName && setShopName(b.shopName))
      .catch(() => {})
    const t = setInterval(() => setClock(new Date()), 1000)
    return () => clearInterval(t)
  }, [])

  useEffect(() => { document.title = `${shopName} | Canlı Altın ve Döviz` }, [shopName])

  const items = message?.items ?? []

  return (
    <div className="board">
      <header className="board-head">
        <div className="brand">
          <span className="brand-mark">ÖD</span>
          <div>
            <h1>{shopName}</h1>
            <p>Canlı altın ve döviz fiyatları</p>
          </div>
        </div>
        <div className="board-meta">
          <span className={`live-dot ${state}`} />
          <span>{state === 'live' ? (message?.stale ? 'Gecikmeli' : 'Canlı') : state === 'reconnecting' ? 'Yeniden bağlanıyor' : state === 'offline' ? 'Bağlantı yok' : 'Bağlanıyor'}</span>
          <span className="sep">·</span>
          <span className="mono">{clock.toLocaleTimeString('tr-TR')}</span>
        </div>
      </header>

      <main className="board-grid">
        {groups.map((g) => {
          const rows = items.filter((i) => g.categories.includes(i.category))
          return (
            <section key={g.title} className="board-card">
              <h2>{g.title}</h2>
              <table className="price-table">
                <thead>
                  <tr>
                    <th>Birim</th>
                    <th className="num">Alış</th>
                    <th className="num">Satış</th>
                    <th aria-label="Yön" />
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr><td colSpan={4} className="empty">{message ? 'Bu grupta ürün yok.' : 'Fiyatlar yükleniyor…'}</td></tr>
                  )}
                  {rows.map((r) => (
                    <PriceRow key={r.code} item={r} flash={flashes[r.code]} />
                  ))}
                </tbody>
              </table>
            </section>
          )
        })}
      </main>

      <footer className="board-foot">
        <span>Son güncelleme: {time(message?.updatedUtc)}</span>
        <span className="sep">·</span>
        <span>Fiyatlar bilgi amaçlıdır; işlem öncesi teyit ediniz.</span>
        <Link to="/admin" className="foot-link">Yönetim</Link>
      </footer>
    </div>
  )
}

function PriceRow({ item, flash }: { item: PublicPriceItem; flash?: 'up' | 'down' }) {
  return (
    <tr className={flash ? `flash-${flash}` : undefined}>
      <td>
        <div className="unit-name">{item.name}</div>
        <div className="unit-cat">{categoryLabels[item.category] ?? item.category}</div>
      </td>
      <td className="num mono">{money(item.buy, item.decimals)}</td>
      <td className="num mono strong">{money(item.sell, item.decimals)}</td>
      <td className={`dir ${item.direction}`}>{item.direction === 'up' ? '▲' : item.direction === 'down' ? '▼' : '•'}</td>
    </tr>
  )
}

/** Satış fiyatı değişen satırları 1,2 sn işaretler. */
function useFlashes(items: PublicPriceItem[]) {
  const prev = useRef(new Map<string, number>())
  const [flashes, setFlashes] = useState<Record<string, 'up' | 'down'>>({})

  const changed = useMemo(() => {
    const next: Record<string, 'up' | 'down'> = {}
    for (const i of items) {
      const old = prev.current.get(i.code)
      if (old !== undefined && old !== i.sell) next[i.code] = i.sell > old ? 'up' : 'down'
    }
    return next
  }, [items])

  useEffect(() => {
    for (const i of items) prev.current.set(i.code, i.sell)
    if (Object.keys(changed).length === 0) return
    setFlashes(changed)
    const t = setTimeout(() => setFlashes({}), 1200)
    return () => clearTimeout(t)
  }, [items, changed])

  return flashes
}
