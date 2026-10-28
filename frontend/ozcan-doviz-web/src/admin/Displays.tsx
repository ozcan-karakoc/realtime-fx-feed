import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { api, ApiError, json, type DisplayKind, type DisplayOutput, type DisplayScreen, type FramePreview, type PublicPriceItem } from '../api'
import { dateTime } from '../format'

const kindLabels: Record<DisplayKind, string> = { Tv: 'TV', Pano: 'Pano', Led: 'LED' }
const outputLabels: Record<DisplayOutput, string> = { Hdmi: 'HDMI (tarayıcı ekranı)', Serial: 'COM / seri port', Bluetooth: 'Bluetooth (BLE)' }

function blank(kind: DisplayKind, output: DisplayOutput, n: number): DisplayScreen {
  const base = { tv: 'tv', pano: 'pano', led: 'led' }[kind.toLowerCase() as 'tv' | 'pano' | 'led']
  return {
    id: `${base}-${n}`,
    name: `${kindLabels[kind]} ${n}`,
    kind,
    output,
    enabled: true,
    layout: {
      title: '', theme: kind === 'Pano' ? 'gold' : 'dark', orientation: kind === 'Pano' ? 'portrait' : 'landscape',
      columns: kind === 'Tv' ? 2 : 1, fontScale: kind === 'Pano' ? 1.3 : 1,
      showLogo: true, showClock: true, showTicker: kind === 'Tv', showBuy: true, showSell: true,
      productCodes: [], pageSize: 0, pageSeconds: 10,
    },
    serial: {
      baudRate: 9600, dataBits: 8, parity: 'none', stopBits: 1, encoding: 'ascii', asciiFold: true, upperCase: true,
      headerTemplate: '', lineTemplate: '{code,-8}{buy,10}{sell,10}', lineSeparator: '\\r\\n',
      prefixHex: '', suffixHex: '', numberCulture: 'invariant', maxLineLength: 0, intervalSeconds: 5,
    },
    bluetooth: { serviceUuid: '0000ffe0-0000-1000-8000-00805f9b34fb', characteristicUuid: '0000ffe1-0000-1000-8000-00805f9b34fb', chunkSize: 20 },
  }
}

const presets: { label: string; kind: DisplayKind; output: DisplayOutput; hint: string }[] = [
  { label: 'TV (HDMI)', kind: 'Tv', output: 'Hdmi', hint: 'Mini PC / TV kutusu tarayıcısında tam ekran' },
  { label: 'Pano (HDMI, dikey)', kind: 'Pano', output: 'Hdmi', hint: 'Dikey monitör veya totem' },
  { label: 'LED (COM)', kind: 'Led', output: 'Serial', hint: 'RS-232 / RS-485 / USB-seri, klasik Bluetooth SPP' },
  { label: 'LED (Bluetooth BLE)', kind: 'Led', output: 'Bluetooth', hint: 'BLE LED denetleyicisi' },
]

/** TV / pano / LED ekranlarını tasarla, önizle, ekranı veya göndericiyi aç. */
export function Displays() {
  const [screens, setScreens] = useState<DisplayScreen[]>([])
  const [edit, setEdit] = useState<DisplayScreen | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [items, setItems] = useState<PublicPriceItem[]>([])
  const [preview, setPreview] = useState<FramePreview | null>(null)
  const [message, setMessage] = useState<{ ok: boolean; text: string; details?: string[] } | null>(null)
  const [frameKey, setFrameKey] = useState(0)

  const load = () => api<DisplayScreen[]>('/api/v1/admin/displays').then(setScreens)
  useEffect(() => {
    void load()
    void fetch('/api/v1/prices').then((r) => r.json()).then((b) => setItems(b.items ?? []))
  }, [])

  // Seri/BLE çıktısı için kaydetmeden canlı çerçeve önizlemesi.
  useEffect(() => {
    if (!edit || edit.output === 'Hdmi') { setPreview(null); return }
    const t = setTimeout(() => {
      api<FramePreview>('/api/v1/admin/displays/preview', { method: 'POST', body: json(edit) })
        .then(setPreview)
        .catch((e) => setPreview({ text: (e as ApiError).details?.join('\n') || (e as Error).message, hex: '', length: 0 }))
    }, 400)
    return () => clearTimeout(t)
  }, [edit])

  function start(kind: DisplayKind, output: DisplayOutput) {
    let n = 1
    while (screens.some((s) => s.id === blank(kind, output, n).id)) n++
    setEdit(blank(kind, output, n))
    setIsNew(true)
    setMessage(null)
  }

  async function save() {
    if (!edit) return
    setMessage(null)
    try {
      const saved = isNew
        ? await api<DisplayScreen>('/api/v1/admin/displays', { method: 'POST', body: json(edit) })
        : await api<DisplayScreen>(`/api/v1/admin/displays/${edit.id}`, { method: 'PUT', body: json(edit) })
      setEdit(saved)
      setIsNew(false)
      setFrameKey((k) => k + 1)
      setMessage({ ok: true, text: 'Kaydedildi. Açık ekranlar 30 sn içinde yeni düzene geçer.' })
      void load()
    } catch (e) {
      const err = e as ApiError
      setMessage({ ok: false, text: err.message, details: err.details })
    }
  }

  async function remove() {
    if (!edit || isNew || !confirm(`${edit.name} silinsin mi?`)) return
    await api(`/api/v1/admin/displays/${edit.id}`, { method: 'DELETE' })
    setEdit(null)
    void load()
  }

  const L = <K extends keyof DisplayScreen['layout']>(k: K, v: DisplayScreen['layout'][K]) => edit && setEdit({ ...edit, layout: { ...edit.layout, [k]: v } })
  const S = <K extends keyof DisplayScreen['serial']>(k: K, v: DisplayScreen['serial'][K]) => edit && setEdit({ ...edit, serial: { ...edit.serial, [k]: v } })
  const B = <K extends keyof DisplayScreen['bluetooth']>(k: K, v: DisplayScreen['bluetooth'][K]) => edit && setEdit({ ...edit, bluetooth: { ...edit.bluetooth, [k]: v } })

  return (
    <div className="page">
      <section className="panel">
        <div className="form-title">TV / Pano / LED Ekranları</div>
        <div className="pad">
          <p className="muted">
            <strong>HDMI:</strong> ekrana bağlı bilgisayarda ekran adresini tarayıcıda tam ekran açın.
            <strong> COM / Bluetooth:</strong> LED panele bağlı bilgisayarda Chrome veya Edge ile <em>Gönderici</em> sayfasını açıp cihazı seçin;
            fiyatlar seçtiğiniz biçimde panele yazılır.
          </p>
          <div className="preset-row">
            {presets.map((p) => (
              <button key={p.label} className="preset" onClick={() => start(p.kind, p.output)}>
                <strong>+ {p.label}</strong>
                <span>{p.hint}</span>
              </button>
            ))}
          </div>
          <div className="screen-list">
            {screens.map((s) => (
              <button key={s.id} className={`screen-item ${edit?.id === s.id && !isNew ? 'active' : ''}`} onClick={() => { setEdit(s); setIsNew(false); setMessage(null) }}>
                <span className={`kind-badge ${s.kind.toLowerCase()}`}>{kindLabels[s.kind]}</span>
                <span className="screen-name">{s.name}</span>
                <span className="muted small">{outputLabels[s.output]} · /ekran/{s.id}</span>
                {!s.enabled && <span className="status-pill cancelled">Kapalı</span>}
              </button>
            ))}
            {screens.length === 0 && <p className="muted">Henüz ekran yok. Yukarıdan bir tür seçin.</p>}
          </div>
        </div>
      </section>

      {edit && (
        <section className="panel form-panel">
          <div className="form-title">{isNew ? 'Yeni ekran' : edit.name}</div>
          <div className="toolbar">
            {!isNew && (
              <>
                <a className="btn" href={`/ekran/${edit.id}`} target="_blank" rel="noopener">Ekranı aç ↗</a>
                {edit.output !== 'Hdmi' && <a className="btn" href={`/ekran/${edit.id}/gonder`} target="_blank" rel="noopener">Gönderici aç ↗</a>}
                <span className="muted small">Son kayıt: {dateTime(edit.updatedUtc)}</span>
              </>
            )}
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

          <div className="pad display-editor">
            <div>
              <h3 className="sub-title">Genel</h3>
              <div className="form-row">
                <label className="field"><span>Ad</span><input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
                <label className="field"><span>Kimlik (adres)</span><input value={edit.id} disabled={!isNew} onChange={(e) => setEdit({ ...edit, id: e.target.value.toLowerCase() })} /></label>
              </div>
              <div className="form-row">
                <label className="field"><span>Tür</span>
                  <select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value as DisplayKind })}>
                    {Object.entries(kindLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                  </select>
                </label>
                <label className="field"><span>Bağlantı</span>
                  <select value={edit.output} onChange={(e) => setEdit({ ...edit, output: e.target.value as DisplayOutput })}>
                    {Object.entries(outputLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                  </select>
                </label>
                <label className="mini check big"><input type="checkbox" checked={edit.enabled} onChange={(e) => setEdit({ ...edit, enabled: e.target.checked })} /> Açık</label>
              </div>

              <h3 className="sub-title">Ürünler ve sıra</h3>
              <ProductPicker items={items} codes={edit.layout.productCodes} onChange={(c) => L('productCodes', c)} />

              {edit.output === 'Hdmi' ? (
                <>
                  <h3 className="sub-title">Görünüm</h3>
                  <label className="field"><span>Başlık (boşsa dükkan adı)</span><input value={edit.layout.title} maxLength={80} onChange={(e) => L('title', e.target.value)} /></label>
                  <div className="form-row">
                    <label className="field"><span>Tema</span>
                      <select value={edit.layout.theme} onChange={(e) => L('theme', e.target.value as DisplayScreen['layout']['theme'])}>
                        <option value="dark">Koyu</option><option value="gold">Altın</option><option value="light">Açık</option>
                      </select>
                    </label>
                    <label className="field"><span>Yön</span>
                      <select value={edit.layout.orientation} onChange={(e) => L('orientation', e.target.value as DisplayScreen['layout']['orientation'])}>
                        <option value="landscape">Yatay</option><option value="portrait">Dikey (ekran 90° döndürülmüş)</option>
                      </select>
                    </label>
                    <label className="field"><span>Sütun</span><input type="number" min={1} max={4} value={edit.layout.columns} onChange={(e) => L('columns', Number(e.target.value))} /></label>
                    <label className="field"><span>Yazı ölçeği</span><input type="number" min={0.5} max={3} step={0.1} value={edit.layout.fontScale} onChange={(e) => L('fontScale', Number(e.target.value))} /></label>
                  </div>
                  <div className="form-row">
                    <label className="field"><span>Sayfa başına satır (0 = hepsi)</span><input type="number" min={0} max={60} value={edit.layout.pageSize} onChange={(e) => L('pageSize', Number(e.target.value))} /></label>
                    <label className="field"><span>Sayfa süresi (sn)</span><input type="number" min={3} max={300} value={edit.layout.pageSeconds} onChange={(e) => L('pageSeconds', Number(e.target.value))} /></label>
                  </div>
                  <div className="row-actions wrap">
                    {([['showLogo', 'Logo'], ['showClock', 'Saat'], ['showTicker', 'Kayan yazı'], ['showBuy', 'Alış'], ['showSell', 'Satış']] as const).map(([k, label]) => (
                      <label key={k} className="mini check big"><input type="checkbox" checked={edit.layout[k]} onChange={(e) => L(k, e.target.checked)} /> {label}</label>
                    ))}
                  </div>
                </>
              ) : (
                <>
                  <h3 className="sub-title">LED çıktı biçimi</h3>
                  <label className="field"><span>Satır şablonu — alanlar: {'{code} {name} {buy} {sell} {dir}'}, genişlik: {'{name,-12}'} sola, {'{sell,10}'} sağa</span>
                    <input className="mono-input" value={edit.serial.lineTemplate} onChange={(e) => S('lineTemplate', e.target.value)} /></label>
                  <label className="field"><span>Başlık satırı (isteğe bağlı) — {'{time} {date} {title}'}</span>
                    <input className="mono-input" value={edit.serial.headerTemplate} onChange={(e) => S('headerTemplate', e.target.value)} /></label>
                  <div className="form-row">
                    <label className="field"><span>Satır ayırıcı</span><input className="mono-input" value={edit.serial.lineSeparator} onChange={(e) => S('lineSeparator', e.target.value)} /></label>
                    <label className="field"><span>Başlangıç baytları (hex)</span><input className="mono-input" value={edit.serial.prefixHex} placeholder="02" onChange={(e) => S('prefixHex', e.target.value)} /></label>
                    <label className="field"><span>Bitiş baytları (hex)</span><input className="mono-input" value={edit.serial.suffixHex} placeholder="03" onChange={(e) => S('suffixHex', e.target.value)} /></label>
                  </div>
                  <div className="form-row">
                    <label className="field"><span>Kodlama</span>
                      <select value={edit.serial.encoding} onChange={(e) => S('encoding', e.target.value)}>
                        <option value="ascii">ASCII</option><option value="windows-1254">Windows-1254 (Türkçe)</option>
                        <option value="iso-8859-9">ISO-8859-9 (Türkçe)</option><option value="utf-8">UTF-8</option>
                      </select>
                    </label>
                    <label className="field"><span>Sayı biçimi</span>
                      <select value={edit.serial.numberCulture} onChange={(e) => S('numberCulture', e.target.value as 'tr' | 'invariant')}>
                        <option value="invariant">6482.44</option><option value="tr">6.482,44</option>
                      </select>
                    </label>
                    <label className="field"><span>Satır uzunluğu (0 = sınırsız)</span><input type="number" min={0} max={500} value={edit.serial.maxLineLength} onChange={(e) => S('maxLineLength', Number(e.target.value))} /></label>
                    <label className="field"><span>Gönderim aralığı (sn)</span><input type="number" min={1} max={3600} value={edit.serial.intervalSeconds} onChange={(e) => S('intervalSeconds', Number(e.target.value))} /></label>
                  </div>
                  <div className="row-actions wrap">
                    <label className="mini check big"><input type="checkbox" checked={edit.serial.asciiFold} onChange={(e) => S('asciiFold', e.target.checked)} /> Türkçe harfleri ASCII'ye çevir</label>
                    <label className="mini check big"><input type="checkbox" checked={edit.serial.upperCase} onChange={(e) => S('upperCase', e.target.checked)} /> BÜYÜK HARF</label>
                  </div>

                  {edit.output === 'Serial' ? (
                    <>
                      <h3 className="sub-title">COM ayarları</h3>
                      <div className="form-row">
                        <label className="field"><span>Baud</span>
                          <select value={edit.serial.baudRate} onChange={(e) => S('baudRate', Number(e.target.value))}>
                            {[1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200].map((b) => <option key={b} value={b}>{b}</option>)}
                          </select>
                        </label>
                        <label className="field"><span>Veri biti</span>
                          <select value={edit.serial.dataBits} onChange={(e) => S('dataBits', Number(e.target.value))}><option value={8}>8</option><option value={7}>7</option></select>
                        </label>
                        <label className="field"><span>Eşlik</span>
                          <select value={edit.serial.parity} onChange={(e) => S('parity', e.target.value as 'none' | 'even' | 'odd')}>
                            <option value="none">Yok</option><option value="even">Çift</option><option value="odd">Tek</option>
                          </select>
                        </label>
                        <label className="field"><span>Durma biti</span>
                          <select value={edit.serial.stopBits} onChange={(e) => S('stopBits', Number(e.target.value))}><option value={1}>1</option><option value={2}>2</option></select>
                        </label>
                      </div>
                    </>
                  ) : (
                    <>
                      <h3 className="sub-title">Bluetooth (BLE) ayarları</h3>
                      <div className="form-row">
                        <label className="field grow2"><span>Servis UUID</span><input className="mono-input" value={edit.bluetooth.serviceUuid} onChange={(e) => B('serviceUuid', e.target.value)} /></label>
                        <label className="field grow2"><span>Karakteristik UUID</span><input className="mono-input" value={edit.bluetooth.characteristicUuid} onChange={(e) => B('characteristicUuid', e.target.value)} /></label>
                        <label className="field"><span>Paket (bayt)</span><input type="number" min={1} max={512} value={edit.bluetooth.chunkSize} onChange={(e) => B('chunkSize', Number(e.target.value))} /></label>
                      </div>
                      <p className="muted small">Varsayılanlar yaygın HM-10 / FFE0 modülleri içindir; cihazınızın belgesindeki değerleri yazın.</p>
                    </>
                  )}
                </>
              )}
            </div>

            <div className="display-preview">
              <h3 className="sub-title">Önizleme</h3>
              {edit.output === 'Hdmi' ? (
                isNew ? <p className="muted">Önizleme için önce kaydedin.</p> : (
                  <PreviewFrame key={frameKey} id={edit.id} portrait={edit.layout.orientation === 'portrait'} />
                )
              ) : (
                <>
                  <pre className="led-preview">{preview?.text ?? '…'}</pre>
                  {preview && preview.hex && <div className="muted small">{preview.length} bayt · <span className="mono-input">{preview.hex.slice(0, 120)}{preview.hex.length > 120 ? '…' : ''}</span></div>}
                </>
              )}
            </div>
          </div>
        </section>
      )}
    </div>
  )
}

/** Ekran sayfasını 1600×900 (dikeyde 900×1600) çizip kutuya sığacak kadar küçültür. */
function PreviewFrame({ id, portrait }: { id: string; portrait: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  const [k, setK] = useState(0.25)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver(() => setK(el.clientWidth / (portrait ? 900 : 1600)))
    ro.observe(el)
    return () => ro.disconnect()
  }, [portrait])
  return (
    <div ref={ref} className={`preview-frame ${portrait ? 'portrait' : ''}`} style={{ '--k': k } as CSSProperties}>
      <iframe src={`/ekran/${id}`} title="Ekran önizleme" />
    </div>
  )
}

function ProductPicker({ items, codes, onChange }: { items: PublicPriceItem[]; codes: string[]; onChange: (c: string[]) => void }) {
  const selected = codes
  const rest = items.filter((i) => !selected.includes(i.code))
  const name = (c: string) => items.find((i) => i.code === c)?.name ?? c
  const move = (i: number, d: number) => {
    const copy = [...selected]
    ;[copy[i], copy[i + d]] = [copy[i + d], copy[i]]
    onChange(copy)
  }
  return (
    <div className="picker">
      <div>
        <div className="muted small">Ekranda ({selected.length === 0 ? 'boş = tüm ürünler' : selected.length})</div>
        <div className="picker-list">
          {selected.map((c, i) => (
            <div key={c} className="picker-row">
              <span>{name(c)}</span>
              <span className="grow" />
              <button className="icon-btn small" disabled={i === 0} onClick={() => move(i, -1)}>↑</button>
              <button className="icon-btn small" disabled={i === selected.length - 1} onClick={() => move(i, 1)}>↓</button>
              <button className="icon-btn small danger" onClick={() => onChange(selected.filter((x) => x !== c))}>✕</button>
            </div>
          ))}
        </div>
      </div>
      <div>
        <div className="muted small">Eklenebilir</div>
        <div className="picker-list">
          {rest.map((i) => (
            <button key={i.code} className="picker-add" onClick={() => onChange([...selected, i.code])}>+ {i.name}</button>
          ))}
        </div>
      </div>
    </div>
  )
}
