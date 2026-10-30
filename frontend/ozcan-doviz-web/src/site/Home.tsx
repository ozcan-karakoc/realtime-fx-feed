import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { InstagramPost } from '../api'
import { money } from '../format'
import { useLive } from '../useLivePrices'
import { Branches } from './Branches'
import { SocialIcon } from './SocialIcon'

const highlightCodes = ['USD', 'EUR', 'GBP', 'HAS', 'GRAM', 'CEYREK_YENI']

/** Firma tanıtım anasayfası: slayt → Instagram → öne çıkan fiyatlar → tanıtım → rakamlar → çağrı. */
export function Home() {
  const { content, message } = useLive()
  const items = message?.items ?? []
  const picked = highlightCodes.map((c) => items.find((i) => i.code === c)).filter((i) => i !== undefined)
  const highlights = picked.length > 0 ? picked : items.slice(0, 6)
  const home = content?.home

  return (
    <div className="home">
      <Slider />

      <InstagramFeed />

      <section className="section">
        <div className="section-head">
          <h2>Öne Çıkan Fiyatlar</h2>
          <Link to="/canli-piyasalar" className="more">Tüm piyasalar →</Link>
        </div>
        <div className="highlight-grid">
          {highlights.map((i) => (
            <div key={i.code} className="highlight">
              <div className="hl-name">{i.name}</div>
              <div className="hl-row"><span>Alış</span><span className="mono">{money(i.buy, i.decimals)}</span></div>
              <div className="hl-row"><span>Satış</span><strong className="mono">{money(i.sell, i.decimals)}</strong></div>
              <span className={`dir ${i.direction}`}>{i.direction === 'up' ? '▲' : i.direction === 'down' ? '▼' : '•'}</span>
            </div>
          ))}
          {highlights.length === 0 && <div className="muted-light">Fiyatlar yükleniyor…</div>}
        </div>
      </section>

      {home && (home.introTitle || home.features.length > 0) && (
        <section className="section intro">
          <div className="intro-text">
            <span className="eyebrow">{content?.shopName}</span>
            {home.introTitle && <h2>{home.introTitle}</h2>}
            {home.introText && <p>{home.introText}</p>}
            <Link to="/hakkimizda" className="more">Bizi tanıyın →</Link>
            {/* Panelden fotoğraf yüklenmediyse demo görsel. */}
            <img src={home.introImageUrl || '/demo/intro.jpg'} alt="" className="intro-img" loading="lazy" />
          </div>
          {home.features.length > 0 && (
            <div className="feature-grid">
              {home.features.map((f, i) => (
                <div key={i} className="feature">
                  <span className="feature-icon" aria-hidden>{f.icon}</span>
                  <strong>{f.title}</strong>
                  <p>{f.text}</p>
                </div>
              ))}
            </div>
          )}
        </section>
      )}

      {home && home.stats.length > 0 && (
        <section className="stats-band">
          {home.stats.map((s, i) => (
            <div key={i} className="stat-item">
              <span className="stat-number mono">{s.value}</span>
              <span className="stat-text">{s.label}</span>
            </div>
          ))}
        </section>
      )}

      <Branches compact />

      {home && (home.ctaTitle || home.ctaText) && (
        <section className="section">
          <div className="cta">
            <div>
              {home.ctaTitle && <h2>{home.ctaTitle}</h2>}
              {home.ctaText && <p>{home.ctaText}</p>}
            </div>
            <Link to="/iletisim" className="hero-btn">İletişime geçin</Link>
          </div>
        </section>
      )}
    </div>
  )
}

/** Instagram son gönderileri. Anahtar yoksa ve profil adresi varsa "takip edin" kartı gösterilir. */
function InstagramFeed() {
  const { content } = useLive()
  const section = content?.instagram
  const [posts, setPosts] = useState<InstagramPost[] | null>(null)

  useEffect(() => {
    if (!section?.enabled) return
    let alive = true
    fetch('/api/v1/content/instagram')
      .then((r) => (r.ok ? r.json() : []))
      .then((p: InstagramPost[]) => alive && setPosts(p))
      .catch(() => alive && setPosts([]))
    return () => { alive = false }
  }, [section?.enabled, content?.updatedUtc])

  if (!section?.enabled || posts === null) return null
  if (posts.length === 0 && !section.profileUrl) return null

  return (
    <section className="section insta">
      <div className="section-head">
        <h2><span className="insta-badge"><SocialIcon type="Instagram" size={20} /></span>{section.title}</h2>
        {section.profileUrl && <a href={section.profileUrl} target="_blank" rel="noopener noreferrer" className="more">Takip et →</a>}
      </div>
      {posts.length > 0 ? (
        <div className="insta-grid">
          {posts.map((p) => (
            <a key={p.id} href={p.permalink} target="_blank" rel="noopener noreferrer" className="insta-tile">
              <img src={p.imageUrl} alt={p.caption?.slice(0, 80) ?? 'Instagram gönderisi'} loading="lazy" referrerPolicy="no-referrer" />
              {p.mediaType === 'VIDEO' && <span className="insta-video" aria-label="Video">▶</span>}
              {p.caption && <span className="insta-caption">{p.caption}</span>}
            </a>
          ))}
        </div>
      ) : (
        <a href={section.profileUrl!} target="_blank" rel="noopener noreferrer" className="insta-follow">
          <SocialIcon type="Instagram" size={32} />
          <span>
            <strong>Instagram'da bizi takip edin</strong>
            <span>Kampanyalar ve yeni ürünler önce orada.</span>
          </span>
        </a>
      )}
    </section>
  )
}

function Slider() {
  const { content } = useLive()
  const slides = content?.slides ?? []
  const [index, setIndex] = useState(0)

  useEffect(() => {
    if (slides.length < 2) return
    const t = setInterval(() => setIndex((i) => (i + 1) % slides.length), 6000)
    return () => clearInterval(t)
  }, [slides.length])
  useEffect(() => { if (index >= slides.length) setIndex(0) }, [index, slides.length])

  if (slides.length === 0) {
    return (
      <section className="hero hero-empty">
        <div className="hero-text">
          <h1>{content?.shopName ?? 'DEMO'}</h1>
          <p>Güncel altın ve döviz fiyatları, anlık olarak.</p>
          <Link to="/canli-piyasalar" className="hero-btn">Canlı Piyasalar</Link>
        </div>
      </section>
    )
  }

  const go = (d: number) => setIndex((i) => (i + d + slides.length) % slides.length)

  return (
    <section className="hero" aria-roledescription="carousel">
      {slides.map((s, i) => (
        <div key={s.id} className={`slide ${i === index ? 'active' : ''}`} aria-hidden={i !== index}>
          <img src={s.imageUrl} alt={s.title} loading={i === 0 ? 'eager' : 'lazy'} />
          {(s.title || s.subtitle) && (
            <div className="hero-text">
              {s.title && <h1>{s.title}</h1>}
              {s.subtitle && <p>{s.subtitle}</p>}
            </div>
          )}
        </div>
      ))}
      {slides.length > 1 && (
        <>
          <button className="hero-nav prev" onClick={() => go(-1)} aria-label="Önceki">‹</button>
          <button className="hero-nav next" onClick={() => go(1)} aria-label="Sonraki">›</button>
          <div className="hero-dots">
            {slides.map((s, i) => (
              <button key={s.id} className={i === index ? 'active' : ''} onClick={() => setIndex(i)} aria-label={`${i + 1}. slayt`} />
            ))}
          </div>
        </>
      )}
    </section>
  )
}
