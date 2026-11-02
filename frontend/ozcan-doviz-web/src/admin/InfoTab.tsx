import { useState } from 'react'
import { api, type InfoDocument, type InfoRow, type InfoSection, type InfoTable, type MediaFile, type SiteContent } from '../api'

const ACCEPT = '.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.jpg,.jpeg,.png'
const newId = () => Math.random().toString(36).slice(2, 10)

function move<T>(list: T[], index: number, delta: number) {
  const next = [...list]
  const target = index + delta
  if (target < 0 || target >= next.length) return next
  ;[next[index], next[target]] = [next[target], next[index]]
  return next
}

/** Site İçeriği → Bilgi Toplum Hizmetleri: serbest tablolar ve doküman listesi. */
export function InfoTab({ content, patch }: { content: SiteContent; patch: (p: Partial<SiteContent>) => void }) {
  const info = content.info
  const set = (p: Partial<InfoSection>) => patch({ info: { ...info, ...p } })
  const setTable = (id: string, p: Partial<InfoTable>) => set({ tables: info.tables.map((t) => (t.id === id ? { ...t, ...p } : t)) })
  const addTable = () => set({
    tables: [...info.tables, { id: newId(), title: 'Yeni tablo', columns: ['', ''], rows: [{ section: false, cells: ['', ''] }] }],
  })

  return (
    <div>
      <p className="muted">Sitede <a href="/bilgi-toplum-hizmetleri" target="_blank" rel="noopener">{content.pages.infoTitle}</a> sayfasında görünür. Menüden kaldırmak için Menü ve Sayfalar sekmesini kullanın. Değişiklikler Kaydet ile yayınlanır.</p>
      <label className="field">
        <span>Açıklama (isteğe bağlı)</span>
        <textarea rows={3} maxLength={5000} value={info.intro} onChange={(e) => set({ intro: e.target.value })} />
      </label>

      <h3>Tablolar</h3>
      {info.tables.map((t, i) => (
        <TableEditor
          key={t.id}
          table={t}
          onChange={(p) => setTable(t.id, p)}
          onMove={(d) => set({ tables: move(info.tables, i, d) })}
          onRemove={() => { if (confirm(`"${t.title || 'Tablo'}" silinsin mi?`)) set({ tables: info.tables.filter((x) => x.id !== t.id) }) }}
        />
      ))}
      <button className="btn" onClick={addTable}>+ Yeni tablo</button>

      <h3 style={{ marginTop: 28 }}>Dokümanlar</h3>
      <label className="field"><span>Bölüm başlığı</span><input value={info.documentsTitle} maxLength={120} onChange={(e) => set({ documentsTitle: e.target.value })} /></label>
      {info.documents.map((d, i) => (
        <div className="doc-edit" key={d.id}>
          <a className="btn small" href={d.url} target="_blank" rel="noopener noreferrer">Aç</a>
          <input value={d.title} maxLength={200} aria-label="Doküman adı" onChange={(e) => set({ documents: info.documents.map((x) => (x.id === d.id ? { ...x, title: e.target.value } : x)) })} />
          <button className="btn small" disabled={i === 0} aria-label="Yukarı" onClick={() => set({ documents: move(info.documents, i, -1) })}>↑</button>
          <button className="btn small" disabled={i === info.documents.length - 1} aria-label="Aşağı" onClick={() => set({ documents: move(info.documents, i, 1) })}>↓</button>
          <button className="btn small" onClick={() => { if (confirm(`"${d.title}" kaldırılsın mı? Kaydedince dosya da silinir.`)) set({ documents: info.documents.filter((x) => x.id !== d.id) }) }}>Kaldır</button>
        </div>
      ))}
      <DocumentUploader onUploaded={(added) => patch({ info: { ...info, documents: [...info.documents, ...added] } })} />
    </div>
  )
}

function TableEditor({ table, onChange, onMove, onRemove }: {
  table: InfoTable
  onChange: (p: Partial<InfoTable>) => void
  onMove: (delta: number) => void
  onRemove: () => void
}) {
  const count = table.columns.length
  const setRow = (i: number, row: InfoRow) => onChange({ rows: table.rows.map((r, j) => (j === i ? row : r)) })
  const setCell = (i: number, c: number, value: string) => {
    const row = table.rows[i]
    const cells = Array.from({ length: count }, (_, k) => (k === c ? value : row.cells[k] ?? ''))
    setRow(i, { ...row, cells })
  }
  const addColumn = () => onChange({
    columns: [...table.columns, ''],
    rows: table.rows.map((r) => ({ ...r, cells: [...r.cells, ''] })),
  })
  const removeColumn = () => {
    if (count <= 1) return
    const lastUsed = table.rows.some((r) => !r.section && r.cells[count - 1]) || table.columns[count - 1]
    if (lastUsed && !confirm('Son sütundaki bilgiler silinsin mi?')) return
    onChange({ columns: table.columns.slice(0, -1), rows: table.rows.map((r) => ({ ...r, cells: r.cells.slice(0, count - 1) })) })
  }
  const blank = () => Array.from({ length: count }, () => '')

  return (
    <div className="info-edit">
      <div className="info-edit-head">
        <label className="field"><span>Tablo başlığı</span><input value={table.title} maxLength={120} onChange={(e) => onChange({ title: e.target.value })} /></label>
        <button className="btn small" onClick={addColumn} disabled={count >= 8}>+ Sütun</button>
        <button className="btn small" onClick={removeColumn} disabled={count <= 1}>− Sütun</button>
        <button className="btn small" aria-label="Tabloyu yukarı taşı" onClick={() => onMove(-1)}>↑</button>
        <button className="btn small" aria-label="Tabloyu aşağı taşı" onClick={() => onMove(1)}>↓</button>
        <button className="btn small" onClick={onRemove}>Tabloyu sil</button>
      </div>
      <div className="info-grid-wrap">
        <table className="info-grid">
          <thead>
            <tr>
              {table.columns.map((h, c) => (
                <th key={c}>
                  <input
                    value={h}
                    maxLength={120}
                    placeholder={`Sütun ${c + 1} başlığı (isteğe bağlı)`}
                    onChange={(e) => onChange({ columns: table.columns.map((x, k) => (k === c ? e.target.value : x)) })}
                  />
                </th>
              ))}
              <th className="ops" />
            </tr>
          </thead>
          <tbody>
            {table.rows.map((r, i) => (
              <tr key={i} className={r.section ? 'sec' : ''}>
                {r.section ? (
                  <td colSpan={count}><input value={r.cells[0] ?? ''} maxLength={2000} placeholder="Ara başlık" onChange={(e) => setCell(i, 0, e.target.value)} /></td>
                ) : (
                  Array.from({ length: count }, (_, c) => (
                    <td key={c}><textarea rows={1} maxLength={2000} value={r.cells[c] ?? ''} onChange={(e) => setCell(i, c, e.target.value)} /></td>
                  ))
                )}
                <td className="ops">
                  <button className="btn small" aria-label="Satırı yukarı taşı" disabled={i === 0} onClick={() => onChange({ rows: move(table.rows, i, -1) })}>↑</button>{' '}
                  <button className="btn small" aria-label="Satırı aşağı taşı" disabled={i === table.rows.length - 1} onClick={() => onChange({ rows: move(table.rows, i, 1) })}>↓</button>{' '}
                  <button className="btn small" aria-label="Satırı sil" onClick={() => onChange({ rows: table.rows.filter((_, j) => j !== i) })}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="row-actions wrap" style={{ marginTop: 8 }}>
        <button className="btn small" onClick={() => onChange({ rows: [...table.rows, { section: false, cells: blank() }] })}>+ Satır</button>
        <button className="btn small" onClick={() => onChange({ rows: [...table.rows, { section: true, cells: blank() }] })}>+ Ara başlık</button>
      </div>
    </div>
  )
}

function DocumentUploader({ onUploaded }: { onUploaded: (docs: InfoDocument[]) => void }) {
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<string[]>([])

  async function upload(files: File[]) {
    setBusy(true)
    setErrors([])
    const added: InfoDocument[] = []
    const failed: string[] = []
    for (const file of files) {
      try {
        const body = new FormData()
        body.append('file', file)
        const saved = await api<MediaFile>('/api/v1/admin/documents', { method: 'POST', body })
        added.push({ id: newId(), title: file.name.replace(/\.[^.]+$/, ''), url: saved.url, size: saved.size })
      } catch (e) {
        failed.push(`${file.name}: ${(e as Error).message}`)
      }
    }
    if (added.length > 0) onUploaded(added)
    setErrors(failed)
    setBusy(false)
  }

  return (
    <div style={{ marginTop: 10 }}>
      <label className={`btn ${busy ? 'disabled' : ''}`}>
        {busy ? 'Yükleniyor…' : '+ Doküman yükle'}
        <input
          type="file"
          accept={ACCEPT}
          multiple
          hidden
          disabled={busy}
          onChange={(e) => {
            const files = [...(e.target.files ?? [])]
            e.target.value = ''
            if (files.length > 0) void upload(files)
          }}
        />
      </label>
      <p className="muted small">PDF, Word, Excel, PowerPoint, JPG veya PNG; dosya başına en fazla 20 MB. Birden çok dosya seçebilirsiniz.</p>
      {errors.map((e) => <div key={e} className="text-error">{e}</div>)}
    </div>
  )
}
