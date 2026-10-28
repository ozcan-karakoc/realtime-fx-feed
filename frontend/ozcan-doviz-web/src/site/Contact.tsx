import type { ReactNode } from 'react'
import { useLive } from '../useLivePrices'
import { SocialRow } from './FloatingSocial'

const MAP_PREFIX = 'https://www.google.com/maps/embed'

export function Contact() {
  const { content } = useLive()
  if (!content) return <div className="section muted-light">Yükleniyor…</div>
  const c = content.contact
  const whatsapp = c.whatsApp?.replace(/\D/g, '')
  const empty = !c.phone && !c.whatsApp && !c.email && !c.address && !c.workingHours

  return (
    <article className="section page-article">
      <h2>İletişim</h2>
      <div className="contact-grid">
        <div className="contact-card">
          {c.phone && <Row label="Telefon"><a href={`tel:${c.phone.replace(/\s/g, '')}`}>{c.phone}</a></Row>}
          {whatsapp && <Row label="WhatsApp"><a href={`https://wa.me/${whatsapp}`} target="_blank" rel="noopener noreferrer">{c.whatsApp}</a></Row>}
          {c.email && <Row label="E-posta"><a href={`mailto:${c.email}`}>{c.email}</a></Row>}
          {c.address && <Row label="Adres">{c.address}</Row>}
          {c.workingHours && <Row label="Çalışma saatleri">{c.workingHours}</Row>}
          {empty && <p className="muted-light">İletişim bilgileri henüz eklenmedi.</p>}
          {content.social.length > 0 && <Row label="Sosyal medya"><SocialRow /></Row>}
        </div>
        {c.mapEmbedUrl?.startsWith(MAP_PREFIX) && (
          <iframe className="map" title="Harita" src={c.mapEmbedUrl} loading="lazy" referrerPolicy="no-referrer-when-downgrade" />
        )}
      </div>
    </article>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="contact-row">
      <span className="contact-label">{label}</span>
      <span>{children}</span>
    </div>
  )
}
