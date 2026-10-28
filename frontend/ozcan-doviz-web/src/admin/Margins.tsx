import { useEffect, useMemo, useState } from 'react'
import { api, ApiError, json, type MarginDocument, type MarginMode, type MarginSide, type ProductRule, type RawSymbol } from '../api'
import { categoryLabels, dateTime, money } from '../format'

const modes: { value: MarginMode; label: string; hint: string }[] = [
  { value: 'Multiply', label: 'Katsayı ×', hint: 'Kaynak × değer (milyem: 0,9160)' },
  { value: 'Add', label: 'TL +', hint: 'Kaynak + değer (eksi düşer)' },
  { value: 'Percent', label: 'Yüzde %', hint: 'Kaynak × (1 + değer/100)' },
]

// Liste anahtarı: ürün kodu düzenlenebildiği için ayrı, yalnızca istemcide tutulan kimlik.
type Row = ProductRule & { _key: string }
let seq = 0
const withKeys = (list: ProductRule[]): Row[] => list.map((p) => ({ ...p, _key: `r${++seq}` }))

function adjust(source: number, side: MarginSide) {
  switch (side.mode) {
    case 'Multiply': return source * side.value
    case 'Percent': return source * (1 + side.value / 100)
    default: return source + side.value
  }
}

/**
 * Fiyat Güncelleme Formu: her ürün için alış (yeşil) ve satış (kırmızı) marjı.
 * Kayıt sunucuda App_Data/margins.json dosyasına yazılır.
 */
export function Margins() {
  const [doc, setDoc] = useState<MarginDocument | null>(null)
  const [products, setProducts] = useState<Row[]>([])
  const [symbols, setSymbols] = useState<RawSymbol[]>([])
  const [filter, setFilter] = useState('all')
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string; details?: string[] } | null>(null)

  useEffect(() => {
    api<MarginDocument>('/api/v1/admin/margins').then((d) => { setDoc(d); setProducts(withKeys(d.products)) })
    const loadSymbols = () => api<RawSymbol[]>('/api/v1/admin/symbols').then(setSymbols).catch(() => {})
    void loadSymbols()
    const t = setInterval(loadSymbols, 5000)
    return () => clearInterval(t)
  }, [])

  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const bySymbol = useMemo(() => new Map(symbols.map((s) => [s.symbol.toUpperCase(), s])), [symbols])
  const sorted = useMemo(
    () => products.map((p, index) => ({ p, index })).sort((a, b) => a.p.order - b.p.order),
    [products],
  )

  function update(index: number, patch: Partial<ProductRule>) {
    setProducts((list) => list.map((p, i) => (i === index ? { ...p, ...patch } : p)))
    setDirty(true)
  }

  function remove(index: number) {
    const p = products[index]
    if (!confirm(`${p.name || p.code} silinsin mi? (Kaydet'e basana kadar dosyaya yazılmaz.)`)) return
    setProducts((list) => list.filter((_, i) => i !== index))
    setDirty(true)
  }

  function addProduct() {
    const order = Math.max(0, ...products.map((p) => p.order)) + 1
    setProducts((list) => [
      ...list,
      {
        _key: `r${++seq}`, code: `URUN${order}`, name: 'Yeni ürün', category: 'altin', sourceSymbol: 'HASALTIN', decimals: 2, visible: true, order,
        buy: { mode: 'Add', value: 0 }, sell: { mode: 'Add', value: 0 },
      },
    ])
    setDirty(true)
  }

  function move(index: number, delta: number) {
    const ordered = [...sorted]
    const pos = ordered.findIndex((x) => x.index === index)
    const other = ordered[pos + delta]
    if (!other) return
    const a = products[index].order
    const b = products[other.index].order
    setProducts((list) => list.map((p, i) => (i === index ? { ...p, order: b } : i === other.index ? { ...p, order: a } : p)))
    setDirty(true)
  }

  async function save() {
    if (!doc) return
    setSaving(true)
    setMessage(null)
    try {
      // Sıra numaralarını 1..n olarak sıkıştır.
      const normalized: ProductRule[] = sorted.map(({ p }, i) => {
        const { _key: _, ...rule } = p
        return { ...rule, order: i + 1 }
      })
      const saved = await api<MarginDocument>('/api/v1/admin/margins', { method: 'PUT', body: json({ ...doc, products: normalized }) })
      setDoc(saved)
      setProducts(withKeys(saved.products))
      setDirty(false)
      setMessage({ kind: 'ok', text: 'Marjlar kaydedildi; fiyatlar hemen güncellendi.' })
    } catch (e) {
      const err = e as ApiError
      setMessage({ kind: 'error', text: err.message, details: err.details })
    } finally {
      setSaving(false)
    }
  }

  if (!doc) return <div className="page"><div className="panel pad">Yükleniyor…</div></div>

  return (
    <div className="page">
      <section className="panel form-panel">
        <div className="form-title">Fiyat Güncelleme Formu</div>

        <div className="toolbar">
          <select value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Kategori">
            <option value="all">Tüm ürünler</option>
            {Object.entries(categoryLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
          <button className="btn" onClick={addProduct}>+ Ürün ekle</button>
          <span className="grow" />
          <span className="muted small">Son kayıt: {dateTime(doc.updatedUtc)}{doc.updatedBy ? ` · ${doc.updatedBy}` : ''}</span>
          {dirty && <span className="badge warn">Kaydedilmedi</span>}
          <button className="btn primary" onClick={save} disabled={saving || !dirty}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</button>
        </div>

        {message && (
          <div className={`alert ${message.kind === 'ok' ? 'ok' : 'error'}`}>
            {message.text}
            {message.details && message.details.length > 0 && <ul>{message.details.map((d) => <li key={d}>{d}</li>)}</ul>}
          </div>
        )}

        <datalist id="symbols">
          {symbols.map((s) => <option key={s.symbol} value={s.symbol}>{s.name ?? ''}</option>)}
        </datalist>

        <div className="margin-grid">
          {sorted
            .filter(({ p }) => filter === 'all' || p.category === filter)
            .map(({ p, index }) => {
              const src = bySymbol.get(p.sourceSymbol.toUpperCase())
              return (
                <div key={p._key} className={`margin-card ${p.visible ? '' : 'hidden-card'}`}>
                  <div className="margin-card-head">
                    <input className="name-input" value={p.name} onChange={(e) => update(index, { name: e.target.value })} aria-label="Ürün adı" />
                    <div className="row-actions">
                      <button className="icon-btn small" onClick={() => move(index, -1)} title="Yukarı">↑</button>
                      <button className="icon-btn small" onClick={() => move(index, 1)} title="Aşağı">↓</button>
                      <button className="icon-btn small danger" onClick={() => remove(index)} title="Sil">✕</button>
                    </div>
                  </div>

                  <div className="meta-row">
                    <label className="mini">Kod
                      <input value={p.code} onChange={(e) => update(index, { code: e.target.value.toUpperCase() })} />
                    </label>
                    <label className="mini">Kaynak sembol
                      <input list="symbols" value={p.sourceSymbol} onChange={(e) => update(index, { sourceSymbol: e.target.value.toUpperCase() })}
                        className={src ? '' : 'missing'} />
                    </label>
                    <label className="mini">Kategori
                      <select value={p.category} onChange={(e) => update(index, { category: e.target.value })}>
                        {Object.entries(categoryLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                      </select>
                    </label>
                    <label className="mini narrow">Ondalık
                      <input type="number" min={0} max={6} value={p.decimals} onChange={(e) => update(index, { decimals: Number(e.target.value) })} />
                    </label>
                    <label className="mini check">
                      <input type="checkbox" checked={p.visible} onChange={(e) => update(index, { visible: e.target.checked })} /> Yayında
                    </label>
                  </div>

                  <SideField label={`${p.name} Alış`} tone="buy" side={p.buy} onChange={(buy) => update(index, { buy })}
                    source={src?.buy} decimals={p.decimals} />
                  <SideField label={`${p.name} Satış`} tone="sell" side={p.sell} onChange={(sell) => update(index, { sell })}
                    source={src?.sell} decimals={p.decimals} />

                  {!src && <div className="muted small">Kaynak sembol etkin sağlayıcıda yok; ürün yayında görünmez.</div>}
                </div>
              )
            })}
        </div>
      </section>
    </div>
  )
}

function SideField({ label, tone, side, onChange, source, decimals }: {
  label: string
  tone: 'buy' | 'sell'
  side: MarginSide
  onChange: (s: MarginSide) => void
  source?: number
  decimals: number
}) {
  const [text, setText] = useState(String(side.value))
  useEffect(() => { setText(String(side.value)) }, [side.value])

  const result = source !== undefined ? adjust(source, side) : undefined
  const mode = modes.find((m) => m.value === side.mode)

  return (
    <fieldset className={`outlined ${tone}`}>
      <legend>{label}</legend>
      <div className="side-row">
        <select value={side.mode} onChange={(e) => onChange({ ...side, mode: e.target.value as MarginMode })} title={mode?.hint}>
          {modes.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
        <input
          className={`value-input ${tone}`}
          inputMode="decimal"
          value={text}
          onChange={(e) => {
            setText(e.target.value)
            const n = Number(e.target.value.replace(',', '.'))
            if (e.target.value.trim() !== '' && Number.isFinite(n)) onChange({ ...side, value: n })
          }}
        />
      </div>
      <div className="preview">
        {source !== undefined
          ? <>Kaynak {money(source, decimals)} → <strong className={tone}>{money(result!, decimals)}</strong></>
          : <span className="muted">Kaynak fiyat yok</span>}
      </div>
    </fieldset>
  )
}
