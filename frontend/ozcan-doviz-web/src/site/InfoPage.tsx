import { Navigate } from 'react-router-dom'
import type { InfoDocument, InfoTable } from '../api'
import { useLive } from '../useLivePrices'

/** Bilgi Toplum Hizmetleri: panelden girilen tablolar ve dokümanlar. Metinler düz yazıdır, HTML yorumlanmaz. */
export function InfoPage() {
  const { content } = useLive()
  if (!content) return <div className="section muted-light">Yükleniyor…</div>
  if (!content.pages.info) return <Navigate to="/" replace />
  const { intro, tables, documents, documentsTitle } = content.info

  return (
    <article className="section page-article">
      <div className="info-page">
        <h2 style={{ margin: 0 }}>{content.pages.infoTitle}</h2>
        {intro && <p className="info-intro">{intro}</p>}
        {tables.map((t) => <TableCard key={t.id} table={t} />)}
        {documents.length > 0 && (
          <section className="info-card">
            {documentsTitle && <h3>{documentsTitle}</h3>}
            <div className="doc-list">
              {documents.map((d) => <DocumentLink key={d.id} doc={d} />)}
            </div>
          </section>
        )}
        {tables.length === 0 && documents.length === 0 && !intro && <p className="muted-light">Bu sayfa henüz hazırlanıyor.</p>}
      </div>
    </article>
  )
}

function TableCard({ table }: { table: InfoTable }) {
  const count = table.columns.length
  return (
    <section className="info-card">
      {table.title && <h3>{table.title}</h3>}
      <div className="info-scroll">
        <table className={`info-table ${count === 2 ? 'pair' : ''}`}>
          {table.columns.some(Boolean) && (
            <thead><tr>{table.columns.map((h, i) => <th key={i}>{h}</th>)}</tr></thead>
          )}
          <tbody>
            {table.rows.map((r, i) => r.section
              ? <tr key={i} className="info-sec"><td colSpan={count}>{r.cells[0]}</td></tr>
              : <tr key={i}>{r.cells.map((c, j) => <td key={j}>{c}</td>)}</tr>)}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function DocumentLink({ doc }: { doc: InfoDocument }) {
  const ext = doc.url.split('.').pop()?.toUpperCase() ?? ''
  return (
    <a className="doc-link" href={doc.url} target="_blank" rel="noopener noreferrer">
      <span className="doc-type">{ext}</span>
      <span>{doc.title}</span>
      {doc.size > 0 && <span className="doc-size">{fileSize(doc.size)}</span>}
    </a>
  )
}

function fileSize(bytes: number) {
  return bytes < 1024 * 1024
    ? `${Math.max(1, Math.round(bytes / 1024))} KB`
    : `${(bytes / 1024 / 1024).toLocaleString('tr-TR', { maximumFractionDigits: 1 })} MB`
}
