import { useLive } from '../useLivePrices'
import { SocialIcon } from './SocialIcon'

/** Sağ altta, asistan düğmesinin üstünde yuvarlak sosyal medya / iletişim düğmeleri (panelden seçilir). */
export function FloatingSocial() {
  const { content } = useLive()
  const links = (content?.social ?? []).filter((s) => s.floating)
  if (links.length === 0) return null

  return (
    <div className="float-stack">
      {links.map((s) => (
        <a
          key={s.type + s.href}
          href={s.href}
          className={`float-btn ${s.type.toLowerCase()}`}
          target={s.type === 'Phone' ? undefined : '_blank'}
          rel="noopener noreferrer"
          aria-label={s.label}
          title={s.label}
        >
          <SocialIcon type={s.type} />
        </a>
      ))}
    </div>
  )
}

/** Footer ve İletişim sayfasındaki küçük ikon listesi. */
export function SocialRow() {
  const { content } = useLive()
  const links = content?.social ?? []
  if (links.length === 0) return null
  return (
    <div className="social-row">
      {links.map((s) => (
        <a key={s.type + s.href} href={s.href} className={`social-chip ${s.type.toLowerCase()}`}
          target={s.type === 'Phone' ? undefined : '_blank'} rel="noopener noreferrer" aria-label={s.label} title={s.label}>
          <SocialIcon type={s.type} size={18} />
        </a>
      ))}
    </div>
  )
}
