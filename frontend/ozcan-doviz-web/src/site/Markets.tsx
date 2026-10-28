import { useEffect, useMemo, useRef, useState } from 'react'
import type { PublicPriceItem } from '../api'
import { categoryLabels, money, time } from '../format'
import { useLive } from '../useLivePrices'

const groups: { title: string; categories: string[] }[] = [
  { title: 'Altın Fiyatları', categories: ['altin'] },
  { title: 'Döviz Kurları', categories: ['doviz', 'parite', 'maden'] },
]

/** Canlı Piyasalar: tüm ürünler. Değişen satır kısa süre yeşil/kırmızı yanar. */
export function Markets() {
  const { message } = useLive()
  const items = message?.items ?? []
  const flashes = useFlashes(items)

  return (
    <div className="section">
      <div className="section-head">
        <h2>Canlı Piyasalar</h2>
        <span className="muted-light">Son güncelleme: {time(message?.updatedUtc)}</span>
      </div>
      <div className="board-grid">
        {groups.map((g) => {
          const rows = items.filter((i) => g.categories.includes(i.category))
          return (
            <section key={g.title} className="board-card">
              <h3>{g.title}</h3>
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
                  {rows.map((r) => <PriceRow key={r.code} item={r} flash={flashes[r.code]} />)}
                </tbody>
              </table>
            </section>
          )
        })}
      </div>
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
