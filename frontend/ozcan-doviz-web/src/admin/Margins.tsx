import { useEffect, useMemo, useState } from 'react'
import { api, ApiError, json, type FeedSettings, type MarginDocument, type MarginMode, type MarginSide, type ProductRule, type ProviderInfo, type RawSymbol } from '../api'
import { categoryLabels, dateTime, money } from '../format'

const modes: { value: MarginMode; label: string; hint: string }[] = [
  { value: 'Add', label: 'TL +', hint: 'Kaynak + değer (eksi değer düşer)' },
  { value: 'Percent', label: '% Yüzde', hint: 'Kaynak × (1 + değer/100)' },
  { value: 'Multiply', label: '× Katsayı', hint: 'Kaynak × değer (milyem: 0,9160)' },
]

// Liste anahtarı: ürün kodu düzenlenebildiği için ayrı, yalnızca istemcide tutulan kimlik.
type Row = ProductRule & { _key: string }
let seq = 0
const withKeys = (list: ProductRule[]): Row[] => list.map((p) => ({ ...p, _key: `r${++seq}` }))
const strip = ({ _key: _, ...rule }: Row): ProductRule => rule

function adjust(source: number, side: MarginSide) {
  switch (side.mode) {
    case 'Multiply': return source * side.value
    case 'Percent': return source * (1 + side.value / 100)
    default: return source + side.value
  }
}

/**
 * Fiyat Güncelleme: her ürün bir tablo satırı. Alış (yeşil) ve satış (kırmızı) marjı biçim + değer olarak girilir,
 * sonuç anında görünür. Seçili satırlara toplu işlem uygulanır. Kayıt sunucuda App_Data/margins.json dosyasına yazılır.
 */
export function Margins() {
  const [doc, setDoc] = useState<MarginDocument | null>(null)
  const [products, setProducts] = useState<Row[]>([])
  const [original, setOriginal] = useState<Record<string, string>>({})
  const [symbols, setSymbols] = useState<Record<string, RawSymbol[]>>({})
  const [providers, setProviders] = useState<ProviderInfo[]>([])
  const [feed, setFeed] = useState<FeedSettings | null>(null)
  const [category, setCategory] = useState('all')
  const [search, setSearch] = useState('')
  const [onlyVisible, setOnlyVisible] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string; details?: string[] } | null>(null)

  const load = (d: MarginDocument) => {
    const rows = withKeys(d.products)
    setDoc(d)
    setProducts(rows)
    setOriginal(Object.fromEntries(rows.map((r) => [r._key, JSON.stringify(strip(r))])))
    setSelected(new Set())
  }

  useEffect(() => {
    api<MarginDocument>('/api/v1/admin/margins').then(load)
    api<{ settings: FeedSettings; providers: ProviderInfo[] }>('/api/v1/admin/providers').then((r) => {
      setFeed(r.settings)
      setProviders(r.providers)
    })
  }, [])

  // Ürün → kategori → genel sırasıyla hangi sağlayıcı kullanılıyor.
  const providerFor = (p: ProductRule) =>
    p.provider || feed?.categoryProviders?.[p.category] || feed?.activeProvider || ''
  const usedProviders = useMemo(
    () => [...new Set(products.map(providerFor).filter(Boolean))].sort().join(','),
    [products, feed],
  )

  // Kullanılan her sağlayıcının ham sembolleri 5 sn'de bir yenilenir (kaynak listesi ve canlı sonuç).
  useEffect(() => {
    if (!usedProviders) return
    let alive = true
    const loadSymbols = () => {
      for (const key of usedProviders.split(','))
        api<RawSymbol[]>(`/api/v1/admin/symbols?provider=${encodeURIComponent(key)}`)
          .then((list) => alive && setSymbols((s) => ({ ...s, [key]: list })))
          .catch(() => {})
    }
    loadSymbols()
    const t = setInterval(loadSymbols, 5000)
    return () => { alive = false; clearInterval(t) }
  }, [usedProviders])

  const changedKeys = useMemo(() => {
    const set = new Set<string>()
    for (const r of products) if (original[r._key] !== JSON.stringify(strip(r))) set.add(r._key)
    return set
  }, [products, original])
  const removed = Object.keys(original).filter((k) => !products.some((p) => p._key === k)).length
  const dirty = changedKeys.size > 0 || removed > 0

  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const bySymbol = useMemo(() => {
    const map: Record<string, Map<string, RawSymbol>> = {}
    for (const [key, list] of Object.entries(symbols)) map[key] = new Map(list.map((s) => [s.symbol.toUpperCase(), s]))
    return map
  }, [symbols])
  const providerName = (key: string) => providers.find((p) => p.key === key)?.displayName ?? key

  const sorted = useMemo(() => [...products].sort((a, b) => a.order - b.order), [products])
  const visibleRows = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('tr-TR')
    return sorted.filter((p) =>
      (category === 'all' || p.category === category)
      && (!onlyVisible || p.visible)
      && (!q || p.name.toLocaleLowerCase('tr-TR').includes(q) || p.code.toLowerCase().includes(q) || p.sourceSymbol.toLowerCase().includes(q)))
  }, [sorted, category, onlyVisible, search])
  const groups = useMemo(() => {
    const keys = Object.keys(categoryLabels)
    const extra = [...new Set(visibleRows.map((r) => r.category))].filter((c) => !keys.includes(c))
    return [...keys, ...extra]
      .map((c) => ({ category: c, rows: visibleRows.filter((r) => r.category === c) }))
      .filter((g) => g.rows.length > 0)
  }, [visibleRows])

  const update = (key: string, patch: Partial<ProductRule>) =>
    setProducts((list) => list.map((p) => (p._key === key ? { ...p, ...patch } : p)))

  const updateMany = (keys: Set<string>, fn: (p: Row) => Partial<ProductRule>) =>
    setProducts((list) => list.map((p) => (keys.has(p._key) ? { ...p, ...fn(p) } : p)))

  function move(key: string, delta: number) {
    const pos = sorted.findIndex((r) => r._key === key)
    const other = sorted[pos + delta]
    if (!other) return
    const a = sorted[pos].order
    setProducts((list) => list.map((p) => (p._key === key ? { ...p, order: other.order } : p._key === other._key ? { ...p, order: a } : p)))
  }

  function addProduct() {
    const order = Math.max(0, ...products.map((p) => p.order)) + 1
    const cat = category === 'all' ? 'altin' : category
    setProducts((list) => [
      ...list,
      {
        _key: `r${++seq}`, code: `URUN${order}`, name: 'Yeni ürün', category: cat, sourceSymbol: cat === 'doviz' ? 'USDTRY' : 'HASALTIN',
        decimals: 2, visible: true, order, buy: { mode: 'Add', value: 0 }, sell: { mode: 'Add', value: 0 },
      },
    ])
  }

  function removeSelected(keys: Set<string>) {
    if (keys.size === 0 || !confirm(`${keys.size} ürün silinsin mi? (Kaydet'e basana kadar dosyaya yazılmaz.)`)) return
    setProducts((list) => list.filter((p) => !keys.has(p._key)))
    setSelected(new Set())
  }

  async function save() {
    if (!doc) return
    setSaving(true)
    setMessage(null)
    try {
      // Sıra numaralarını 1..n olarak sıkıştır.
      const normalized = sorted.map((r, i) => ({ ...strip(r), order: i + 1 }))
      const saved = await api<MarginDocument>('/api/v1/admin/margins', { method: 'PUT', body: json({ ...doc, products: normalized }) })
      load(saved)
      setMessage({ kind: 'ok', text: 'Marjlar kaydedildi; fiyatlar hemen güncellendi.' })
    } catch (e) {
      const err = e as ApiError
      setMessage({ kind: 'error', text: err.message, details: err.details })
    } finally {
      setSaving(false)
    }
  }

  if (!doc) return <div className="page wide"><div className="panel pad">Yükleniyor…</div></div>

  const allVisibleSelected = visibleRows.length > 0 && visibleRows.every((r) => selected.has(r._key))
  const toggleAll = () => setSelected(allVisibleSelected ? new Set() : new Set(visibleRows.map((r) => r._key)))
  const toggle = (key: string) => setSelected((s) => { const n = new Set(s); if (n.has(key)) n.delete(key); else n.add(key); return n })

  return (
    <div className="page wide">
      <section className="panel">
        <div className="form-title">Fiyat Güncelleme</div>

        <div className="toolbar">
          <input className="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ürün, kod veya sembol ara" />
          <div className="seg">
            <button className={category === 'all' ? 'active' : ''} onClick={() => setCategory('all')}>Tümü ({products.length})</button>
            {Object.entries(categoryLabels).map(([k, v]) => (
              <button key={k} className={category === k ? 'active' : ''} onClick={() => setCategory(k)}>
                {v} ({products.filter((p) => p.category === k).length})
              </button>
            ))}
          </div>
          <label className="mini check big"><input type="checkbox" checked={onlyVisible} onChange={(e) => setOnlyVisible(e.target.checked)} /> Yalnız yayındakiler</label>
          <span className="grow" />
          <button className="btn" onClick={addProduct}>+ Ürün ekle</button>
          {dirty && <span className="badge warn">{changedKeys.size + removed} değişiklik</span>}
          {dirty && <button className="btn" onClick={() => api<MarginDocument>('/api/v1/admin/margins').then(load)}>Geri al</button>}
          <button className="btn primary" onClick={save} disabled={saving || !dirty}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</button>
        </div>

        {selected.size > 0 && (
          <BulkBar
            count={selected.size}
            onVisible={(v) => updateMany(selected, () => ({ visible: v }))}
            onMargin={(side, m) => updateMany(selected, () => ({ [side]: m }))}
            onDecimals={(d) => updateMany(selected, () => ({ decimals: d }))}
            onDelete={() => removeSelected(selected)}
            onClear={() => setSelected(new Set())}
          />
        )}

        {message && (
          <div className={`alert ${message.kind === 'ok' ? 'ok' : 'error'}`}>
            {message.text}
            {message.details && message.details.length > 0 && <ul>{message.details.map((d) => <li key={d}>{d}</li>)}</ul>}
          </div>
        )}

        {Object.entries(symbols).map(([key, list]) => (
          <datalist key={key} id={`symbols-${key}`}>
            {list.map((s) => <option key={s.symbol} value={s.symbol}>{s.name ?? ''}</option>)}
          </datalist>
        ))}

        <div className="table-wrap margin-table-wrap">
          <table className="data-table margin-table">
            <thead>
              <tr>
                <th className="c-check"><input type="checkbox" checked={allVisibleSelected} onChange={toggleAll} aria-label="Tümünü seç" /></th>
                <th className="c-on" title="Yayında">Yayın</th>
                <th>Ürün</th>
                <th>Kategori / Sağlayıcı</th>
                <th>Kaynak</th>
                <th className="buy-h">Alış marjı</th>
                <th className="num buy-h">Alış</th>
                <th className="sell-h">Satış marjı</th>
                <th className="num sell-h">Satış</th>
                <th title="Ondalık basamak">Ond.</th>
                <th />
              </tr>
            </thead>
            {groups.map((g) => (
              <tbody key={g.category}>
                <tr className="group-row">
                  <td colSpan={11}>{categoryLabels[g.category] ?? g.category} <span className="muted small">· {g.rows.length} ürün</span></td>
                </tr>
                {g.rows.map((p) => {
                  const prov = providerFor(p)
                  const src = bySymbol[prov]?.get(p.sourceSymbol.toUpperCase())
                  const categoryDefault = feed?.categoryProviders?.[p.category] || feed?.activeProvider || ''
                  const isNew = !(p._key in original)
                  const changed = changedKeys.has(p._key)
                  return (
                    <tr key={p._key} className={`${p.visible ? '' : 'off'} ${changed ? 'changed' : ''} ${selected.has(p._key) ? 'sel' : ''}`}>
                      <td className="c-check"><input type="checkbox" checked={selected.has(p._key)} onChange={() => toggle(p._key)} aria-label={`${p.name} seç`} /></td>
                      <td className="c-on">
                        <label className="switch" title={p.visible ? 'Yayında' : 'Gizli'}>
                          <input type="checkbox" checked={p.visible} onChange={(e) => update(p._key, { visible: e.target.checked })} />
                          <span />
                        </label>
                      </td>
                      <td className="c-name">
                        <input className="cell-input strong" value={p.name} onChange={(e) => update(p._key, { name: e.target.value })} aria-label="Ürün adı" />
                        <input className="cell-input code" value={p.code} onChange={(e) => update(p._key, { code: e.target.value.toUpperCase() })} aria-label="Ürün kodu" />
                        {isNew && <span className="badge primary">yeni</span>}
                      </td>
                      <td className="c-stack">
                        <select className="cell-select" value={p.category} onChange={(e) => update(p._key, { category: e.target.value })} aria-label="Kategori">
                          {Object.entries(categoryLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                        </select>
                        <select className="cell-select" aria-label="Sağlayıcı" value={p.provider ?? ''} onChange={(e) => update(p._key, { provider: e.target.value || null })}>
                          <option value="">↳ {providerName(categoryDefault)}</option>
                          {providers.map((pr) => <option key={pr.key} value={pr.key}>{pr.displayName}</option>)}
                        </select>
                      </td>
                      <td className="c-src">
                        <input list={`symbols-${prov}`} className={`cell-input code ${src || !symbols[prov] ? '' : 'missing'}`} value={p.sourceSymbol}
                          onChange={(e) => update(p._key, { sourceSymbol: e.target.value.toUpperCase() })} aria-label="Kaynak sembol"
                          title={src ? src.name ?? '' : `${p.sourceSymbol} ${providerName(prov)} listesinde yok`} />
                        <span className="src-price mono">{src ? `${money(src.buy, p.decimals)} / ${money(src.sell, p.decimals)}` : symbols[prov] ? '⚠ sembol yok' : '…'}</span>
                      </td>
                      <td><MarginCell side={p.buy} tone="buy" onChange={(buy) => update(p._key, { buy })} /></td>
                      <td className="num mono result buy">{src ? money(adjust(src.buy, p.buy), p.decimals) : '—'}</td>
                      <td><MarginCell side={p.sell} tone="sell" onChange={(sell) => update(p._key, { sell })} /></td>
                      <td className="num mono result sell">{src ? money(adjust(src.sell, p.sell), p.decimals) : '—'}</td>
                      <td>
                        <select className="cell-select narrow" value={p.decimals} onChange={(e) => update(p._key, { decimals: Number(e.target.value) })}>
                          {[0, 1, 2, 3, 4, 5, 6].map((d) => <option key={d} value={d}>{d}</option>)}
                        </select>
                      </td>
                      <td className="actions">
                        <button className="icon-btn small" onClick={() => move(p._key, -1)} title="Yukarı">↑</button>
                        <button className="icon-btn small" onClick={() => move(p._key, 1)} title="Aşağı">↓</button>
                        <button className="icon-btn small danger" onClick={() => removeSelected(new Set([p._key]))} title="Sil">✕</button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            ))}
            {groups.length === 0 && (
              <tbody><tr><td colSpan={11} className="empty">Bu filtrede ürün yok.</td></tr></tbody>
            )}
          </table>
        </div>
        <div className="table-foot muted small">
          Son kayıt: {dateTime(doc.updatedUtc)}{doc.updatedBy ? ` · ${doc.updatedBy}` : ''} ·
          Biçimler: <b>TL +</b> kaynağa ekler, <b>% Yüzde</b> oranla artırır, <b>× Katsayı</b> çarpar (milyem için).
          Alış kaynağın alışından, satış kaynağın satışından hesaplanır.
        </div>
      </section>
    </div>
  )
}

/** Marj: biçim listesi + değer kutusu. Virgül veya nokta kabul edilir. */
function MarginCell({ side, tone, onChange }: { side: MarginSide; tone: 'buy' | 'sell'; onChange: (s: MarginSide) => void }) {
  const [text, setText] = useState(String(side.value))
  useEffect(() => { setText(String(side.value)) }, [side.value])
  const mode = modes.find((m) => m.value === side.mode)
  return (
    <div className={`margin-cell ${tone}`}>
      <select value={side.mode} title={mode?.hint} onChange={(e) => onChange({ ...side, mode: e.target.value as MarginMode })}>
        {modes.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
      </select>
      <input
        inputMode="decimal"
        value={text}
        aria-label={tone === 'buy' ? 'Alış marjı' : 'Satış marjı'}
        onChange={(e) => {
          setText(e.target.value)
          const n = Number(e.target.value.replace(',', '.'))
          if (e.target.value.trim() !== '' && Number.isFinite(n)) onChange({ ...side, value: n })
        }}
      />
    </div>
  )
}

/** Seçili satırlara toplu işlem. */
function BulkBar({ count, onVisible, onMargin, onDecimals, onDelete, onClear }: {
  count: number
  onVisible: (v: boolean) => void
  onMargin: (side: 'buy' | 'sell', m: MarginSide) => void
  onDecimals: (d: number) => void
  onDelete: () => void
  onClear: () => void
}) {
  const [side, setSide] = useState<'buy' | 'sell' | 'both'>('both')
  const [mode, setMode] = useState<MarginMode>('Add')
  const [value, setValue] = useState('0')
  const n = Number(value.replace(',', '.'))
  const apply = () => {
    if (!Number.isFinite(n)) return
    if (side !== 'sell') onMargin('buy', { mode, value: n })
    if (side !== 'buy') onMargin('sell', { mode, value: n })
  }
  return (
    <div className="bulk-bar">
      <strong>{count} seçili</strong>
      <button className="btn small" onClick={() => onVisible(true)}>Yayına al</button>
      <button className="btn small" onClick={() => onVisible(false)}>Yayından kaldır</button>
      <span className="sep-v" />
      <select value={side} onChange={(e) => setSide(e.target.value as typeof side)} aria-label="Hangi marj">
        <option value="both">Alış + satış</option><option value="buy">Yalnız alış</option><option value="sell">Yalnız satış</option>
      </select>
      <select value={mode} onChange={(e) => setMode(e.target.value as MarginMode)} aria-label="Biçim">
        {modes.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
      </select>
      <input className="bulk-value" inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} aria-label="Değer" />
      <button className="btn small primary" disabled={!Number.isFinite(n)} onClick={apply}>Marjı uygula</button>
      <span className="sep-v" />
      <select defaultValue="" onChange={(e) => { if (e.target.value !== '') onDecimals(Number(e.target.value)); e.target.value = '' }} aria-label="Ondalık">
        <option value="">Ondalık…</option>
        {[0, 1, 2, 3, 4].map((d) => <option key={d} value={d}>{d} basamak</option>)}
      </select>
      <span className="grow" />
      <button className="btn small danger" onClick={onDelete}>Sil</button>
      <button className="btn small" onClick={onClear}>Seçimi kaldır</button>
    </div>
  )
}
