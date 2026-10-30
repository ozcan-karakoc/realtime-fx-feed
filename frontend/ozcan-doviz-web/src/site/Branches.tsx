import { useLive } from '../useLivePrices'

/**
 * Şubeler ve haritaları. Harita/yol tarifi adresleri sunucuda üretilir (yalnızca google.com/maps).
 * compact: Anasayfa ve Hakkımızda'da daha alçak harita.
 */
export function Branches({ title = 'Şubelerimiz', compact = false }: { title?: string; compact?: boolean }) {
  const { content } = useLive()
  const locations = content?.locations ?? []
  if (locations.length === 0) return null

  return (
    <section className="section branches">
      <div className="section-head">
        <h2>{title}</h2>
      </div>
      <div className="branch-grid">
        {locations.map((l) => (
          <article key={l.name} className="branch-card">
            <iframe
              className={`branch-map ${compact ? 'compact' : ''}`}
              title={`${l.name} haritası`}
              src={l.embedUrl}
              loading="lazy"
              referrerPolicy="no-referrer-when-downgrade"
            />
            <div className="branch-info">
              <h3>{l.name}</h3>
              {l.address && <p>{l.address}</p>}
              {l.workingHours && <p className="muted-light">{l.workingHours}</p>}
              <div className="branch-actions">
                <a href={l.directionsUrl} target="_blank" rel="noopener noreferrer" className="hero-btn small">Yol tarifi al</a>
                {l.phone && <a href={`tel:${l.phone.replace(/\s/g, '')}`} className="more">{l.phone}</a>}
              </div>
            </div>
          </article>
        ))}
      </div>
    </section>
  )
}
