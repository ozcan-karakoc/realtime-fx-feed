import { useLive } from '../useLivePrices'

export function About() {
  const { content } = useLive()
  if (!content) return <div className="section muted-light">Yükleniyor…</div>
  const paragraphs = content.about.body.split(/\n\s*\n/).map((p) => p.trim()).filter(Boolean)

  return (
    <article className="section page-article">
      <h2>{content.about.title}</h2>
      <div className={`about ${content.about.imageUrl ? 'with-image' : ''}`}>
        <div className="about-text">
          {/* Düz metin: HTML yorumlanmaz. */}
          {paragraphs.map((p, i) => <p key={i}>{p}</p>)}
        </div>
        {content.about.imageUrl && <img src={content.about.imageUrl} alt={content.about.title} className="about-img" />}
      </div>
    </article>
  )
}
