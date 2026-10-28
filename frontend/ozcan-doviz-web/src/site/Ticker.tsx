import { money } from '../format'
import { useLive } from '../useLivePrices'

const priceCodes = ['USD', 'EUR', 'GBP', 'HAS', 'GRAM', 'CEYREK_YENI']

/** Panelden yönetilen kayan yazı. "Fiyatları göster" açıksa öne birkaç canlı fiyat eklenir. */
export function Ticker() {
  const { content, message } = useLive()
  const ticker = content?.ticker
  if (!ticker?.enabled) return null

  const prices = ticker.showPrices
    ? (message?.items ?? [])
        .filter((i) => priceCodes.includes(i.code))
        .map((i) => ({ key: i.code, text: `${i.name}: ${money(i.sell, i.decimals)}`, dir: i.direction }))
    : []
  const texts = ticker.items.map((t, idx) => ({ key: `t${idx}`, text: t, dir: undefined as string | undefined }))
  const parts = [...prices, ...texts]
  if (parts.length === 0) return null

  const row = (copy: number) => (
    <div className="ticker-row" aria-hidden={copy > 0 || undefined}>
      {parts.map((p) => (
        <span key={`${copy}-${p.key}`} className="ticker-item">
          {p.dir && <span className={`dir ${p.dir}`}>{p.dir === 'up' ? '▲' : p.dir === 'down' ? '▼' : '•'}</span>}
          {p.text}
        </span>
      ))}
    </div>
  )

  return (
    <div className="ticker" role="marquee" aria-label="Duyurular">
      <div className="ticker-track" style={{ animationDuration: `${ticker.durationSeconds}s` }}>
        {row(0)}
        {row(1)}
      </div>
    </div>
  )
}
