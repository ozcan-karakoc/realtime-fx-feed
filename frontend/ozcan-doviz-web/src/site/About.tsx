import { useLive } from '../useLivePrices'
import { Branches } from './Branches'

export function About() {
  const { content } = useLive()
  if (!content) return <div className="section muted-light">Yükleniyor…</div>
  const paragraphs = content.about.body.split(/\n\s*\n/).map((p) => p.trim()).filter(Boolean)
  // Panelden fotoğraf yüklenmediyse demo görsel.
  const image = content.about.imageUrl || '/demo/about.jpg'

  return (
    <>
      <article className="section page-article">
        <h2>{content.about.title}</h2>
        <div className="about with-image">
          <div className="about-text">
            {/* Düz metin: HTML yorumlanmaz. */}
            {paragraphs.map((p, i) => <p key={i}>{p}</p>)}
          </div>
          <img src={image} alt={content.about.title} className="about-img" />
        </div>
      </article>
      <Branches title="Bizi ziyaret edin" compact />
    </>
  )
}
