import { useEffect, useState } from 'react'
import { api, ApiError, json, type CustomProvider, type CustomProviderTest } from '../api'

const empty: CustomProvider = {
  key: '', name: '', url: 'wss://', enabled: true, protocol: 'auto', subscribeMessage: '', origin: '',
  dataPath: '', symbolField: 'code', buyField: 'alis', sellField: 'satis', nameField: '', aliases: '',
}

const presets: { label: string; hint: string; value: Partial<CustomProvider> }[] = [
  {
    label: 'socket.io (Harem tarzı)',
    hint: '42["price_changed",{"data":{"USDTRY":{"code":"USDTRY","alis":"..","satis":".."}}}]',
    value: { url: 'wss://ornek.com/socket.io/?EIO=4&transport=websocket', protocol: 'socketio', dataPath: '1.data', symbolField: 'code', buyField: 'alis', sellField: 'satis' },
  },
  {
    label: 'WebSocket, JSON dizi',
    hint: '{"data":[{"symbol":"USDTRY","bid":41.1,"ask":41.2}]}',
    value: { url: 'wss://ornek.com/fiyat', protocol: 'raw', dataPath: 'data', symbolField: 'symbol', buyField: 'bid', sellField: 'ask' },
  },
  {
    label: 'HTTP JSON (yoklama)',
    hint: '[{"kod":"USD","alis":"41,10","satis":"41,20"}]',
    value: { url: 'https://ornek.com/api/fiyatlar', protocol: 'auto', dataPath: '', symbolField: 'kod', buyField: 'alis', sellField: 'satis' },
  },
]

/** Fiyat Sağlayıcıları sayfasının "Kendi sağlayıcın" bölümü. */
export function CustomProviders({ onChanged }: { onChanged: () => void }) {
  const [list, setList] = useState<CustomProvider[]>([])
  const [edit, setEdit] = useState<CustomProvider | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [test, setTest] = useState<CustomProviderTest | 'running' | null>(null)
  const [message, setMessage] = useState<{ ok: boolean; text: string; details?: string[] } | null>(null)

  const load = () => api<CustomProvider[]>('/api/v1/admin/custom-providers').then(setList)
  useEffect(() => { void load() }, [])

  const set = (p: Partial<CustomProvider>) => edit && setEdit({ ...edit, ...p })

  function start(preset?: Partial<CustomProvider>) {
    setEdit({ ...empty, ...preset })
    setIsNew(true)
    setTest(null)
    setMessage(null)
  }

  async function runTest() {
    if (!edit) return
    setTest('running')
    try {
      setTest(await api<CustomProviderTest>('/api/v1/admin/custom-providers/test', { method: 'POST', body: json(edit) }))
    } catch (e) {
      const err = e as ApiError
      setTest({ ok: false, message: [err.message, ...err.details].join(' '), symbols: [] })
    }
  }

  async function save() {
    if (!edit) return
    setMessage(null)
    try {
      const saved = await api<CustomProvider>(`/api/v1/admin/custom-providers/${encodeURIComponent(edit.key)}`, { method: 'PUT', body: json(edit) })
      setEdit(saved)
      setIsNew(false)
      setMessage({ ok: true, text: 'Kaydedildi. Sağlayıcı yukarıdaki listede ve kategori/ürün seçimlerinde kullanılabilir.' })
      void load()
      onChanged()
    } catch (e) {
      const err = e as ApiError
      setMessage({ ok: false, text: err.message, details: err.details })
    }
  }

  async function remove() {
    if (!edit || isNew || !confirm(`${edit.name} silinsin mi?`)) return
    try {
      await api(`/api/v1/admin/custom-providers/${encodeURIComponent(edit.key)}`, { method: 'DELETE' })
      setEdit(null)
      void load()
      onChanged()
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message })
    }
  }

  return (
    <section className="panel form-panel">
      <div className="form-title">Kendi Sağlayıcın (ws:// · wss:// · http)</div>
      <div className="pad">
        <p className="muted">
          Anlaşmalı olduğunuz bir fiyat akışını ekleyin. Sunucu adrese bağlanır, gelen JSON'u aşağıdaki eşlemeyle okur.
          Önce <strong>Dene</strong> ile gelen mesajı görün, veri yolunu ve alan adlarını ona göre ayarlayın.
        </p>

        <div className="screen-list">
          {list.map((p) => (
            <button key={p.key} className={`screen-item ${edit?.key === p.key && !isNew ? 'active' : ''}`}
              onClick={() => { setEdit(p); setIsNew(false); setTest(null); setMessage(null) }}>
              <span className={`kind-badge ${p.url.startsWith('http') ? 'pano' : ''}`}>{p.url.split(':')[0].toUpperCase()}</span>
              <span className="screen-name">{p.name}</span>
              <span className="muted small">{p.key} · {p.url}</span>
              {!p.enabled && <span className="status-pill cancelled">Kapalı</span>}
            </button>
          ))}
        </div>

        <div className="preset-row">
          {presets.map((p) => (
            <button key={p.label} className="preset" onClick={() => start(p.value)}>
              <strong>+ {p.label}</strong>
              <span className="mono-input">{p.hint}</span>
            </button>
          ))}
        </div>

        {edit && (
          <div className="custom-form">
            <div className="form-row">
              <label className="field"><span>Ad</span><input value={edit.name} maxLength={60} onChange={(e) => set({ name: e.target.value })} placeholder="Anlaşmalı akış" /></label>
              <label className="field"><span>Anahtar</span><input value={edit.key} disabled={!isNew} onChange={(e) => set({ key: e.target.value.toLowerCase() })} placeholder="ornek-akis" /></label>
              <label className="mini check big"><input type="checkbox" checked={edit.enabled} onChange={(e) => set({ enabled: e.target.checked })} /> Açık</label>
            </div>
            <div className="form-row">
              <label className="field grow2"><span>Adres (ws://, wss://, http://, https://)</span>
                <input className="mono-input" value={edit.url} onChange={(e) => set({ url: e.target.value })} /></label>
              <label className="field"><span>Protokol</span>
                <select value={edit.protocol} onChange={(e) => set({ protocol: e.target.value as CustomProvider['protocol'] })}>
                  <option value="auto">Otomatik</option><option value="raw">Düz JSON</option><option value="socketio">socket.io (EIO 4)</option>
                </select>
              </label>
            </div>
            <div className="form-row">
              <label className="field grow2"><span>Abonelik mesajı (bağlanınca gönderilir, isteğe bağlı)</span>
                <input className="mono-input" value={edit.subscribeMessage ?? ''} onChange={(e) => set({ subscribeMessage: e.target.value })} placeholder='{"action":"subscribe","channel":"prices"}' /></label>
              <label className="field"><span>Origin başlığı (isteğe bağlı)</span>
                <input className="mono-input" value={edit.origin ?? ''} onChange={(e) => set({ origin: e.target.value })} placeholder="https://www.ornek.com" /></label>
            </div>

            <h3 className="sub-title">Eşleme</h3>
            <div className="form-row">
              <label className="field"><span>Veri yolu (boş = kök)</span><input className="mono-input" value={edit.dataPath} onChange={(e) => set({ dataPath: e.target.value })} placeholder="data / 1.data" /></label>
              <label className="field"><span>Sembol alanı</span><input className="mono-input" value={edit.symbolField} onChange={(e) => set({ symbolField: e.target.value })} /></label>
              <label className="field"><span>Alış alanı</span><input className="mono-input" value={edit.buyField} onChange={(e) => set({ buyField: e.target.value })} /></label>
              <label className="field"><span>Satış alanı</span><input className="mono-input" value={edit.sellField} onChange={(e) => set({ sellField: e.target.value })} /></label>
              <label className="field"><span>Ad alanı (isteğe bağlı)</span><input className="mono-input" value={edit.nameField ?? ''} onChange={(e) => set({ nameField: e.target.value })} /></label>
            </div>
            <label className="field">
              <span>Ortak koda eşle — her satır HAM=ORTAK (ör. ALTIN=HASALTIN, USD=USDTRY). Marj kuralları ortak koda bağlıdır.</span>
              <textarea rows={3} className="mono-input" value={edit.aliases ?? ''} onChange={(e) => set({ aliases: e.target.value })} placeholder={'ALTIN=HASALTIN\nCEYREK_YENI=CEYREK\nUSD=USDTRY'} />
            </label>

            <div className="row-actions wrap">
              <button className="btn" onClick={runTest} disabled={test === 'running'}>{test === 'running' ? 'Deneniyor… (en fazla 10 sn)' : 'Dene'}</button>
              <span className="grow" />
              {!isNew && <button className="btn danger" onClick={remove}>Sil</button>}
              <button className="btn primary" onClick={save}>Kaydet</button>
            </div>

            {message && (
              <div className={`alert ${message.ok ? 'ok' : 'error'}`}>
                {message.text}
                {message.details && message.details.length > 0 && <ul>{message.details.map((d) => <li key={d}>{d}</li>)}</ul>}
              </div>
            )}

            {test && test !== 'running' && (
              <div className="test-result">
                <div className={`alert ${test.ok ? 'ok' : 'error'}`}>{test.message}</div>
                {test.symbols.length > 0 && (
                  <div className="symbol-chips">{test.symbols.map((s) => <span key={s} className="badge primary">{s}</span>)}</div>
                )}
                {test.sample && (
                  <>
                    <div className="muted small">Gelen mesaj (ilk 1500 karakter)</div>
                    <pre className="code">{test.sample}</pre>
                  </>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </section>
  )
}
