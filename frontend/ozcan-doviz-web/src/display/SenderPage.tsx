import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { useLive } from '../useLivePrices'
import { useDisplay } from './DisplayPage'

// Web Serial / Web Bluetooth TypeScript kitaplığında yok; yalnızca kullandığımız kısım.
interface SerialPortLike {
  open(o: { baudRate: number; dataBits: number; stopBits: number; parity: string }): Promise<void>
  close(): Promise<void>
  writable: WritableStream<Uint8Array> | null
}
interface BleCharacteristic {
  writeValueWithoutResponse?(data: Uint8Array): Promise<void>
  writeValue(data: Uint8Array): Promise<void>
}
interface Writer { kind: 'COM' | 'Bluetooth'; label: string; write(bytes: Uint8Array): Promise<void>; close(): Promise<void> }

const nav = navigator as Navigator & {
  serial?: { requestPort(): Promise<SerialPortLike> }
  bluetooth?: { requestDevice(o: unknown): Promise<{ name?: string; gatt?: { connect(): Promise<{ getPrimaryService(u: string): Promise<{ getCharacteristic(u: string): Promise<BleCharacteristic> }>; disconnect?(): void }> } }> }
  wakeLock?: { request(type: 'screen'): Promise<{ release(): Promise<void> }> }
}

const hex = (b: Uint8Array) => Array.from(b.slice(0, 48), (x) => x.toString(16).padStart(2, '0')).join(' ') + (b.length > 48 ? ' …' : '')

/**
 * COM / Bluetooth gönderici: /ekran/{id}/gonder. Dükkandaki bilgisayarda Chrome veya Edge ile açılır.
 * Çerçeveyi sunucudan alır (/api/v1/displays/{id}/frame) ve cihaza olduğu gibi yazar:
 * ayarlanan aralıkta ve fiyat her değiştiğinde (en fazla saniyede bir).
 */
export function SenderPage() {
  const { id } = useParams()
  const { screen, error } = useDisplay(id)
  const { message } = useLive()
  const [writer, setWriter] = useState<Writer | null>(null)
  const [log, setLog] = useState<string[]>([])
  const [preview, setPreview] = useState('')
  const [busy, setBusy] = useState(false)
  const lastSent = useRef(0)
  const sending = useRef(false)

  const add = (line: string) => setLog((l) => [`${new Date().toLocaleTimeString('tr-TR')}  ${line}`, ...l].slice(0, 30))

  const send = useCallback(async (reason: string) => {
    if (!writer || !id || sending.current || Date.now() - lastSent.current < 1000) return
    sending.current = true
    try {
      const r = await fetch(`/api/v1/displays/${encodeURIComponent(id)}/frame`, { cache: 'no-store' })
      if (!r.ok) throw new Error(`Sunucu ${r.status}`)
      const bytes = new Uint8Array(await r.arrayBuffer())
      await writer.write(bytes)
      lastSent.current = Date.now()
      setPreview(new TextDecoder('latin1').decode(bytes))
      add(`${reason}: ${bytes.length} bayt → ${writer.label}  [${hex(bytes)}]`)
    } catch (e) {
      add(`HATA: ${(e as Error).message}`)
    } finally {
      sending.current = false
    }
  }, [writer, id])

  // Aralıkla gönder.
  useEffect(() => {
    if (!writer || !screen) return
    void send('ilk gönderim')
    const t = setInterval(() => void send('aralık'), screen.serial.intervalSeconds * 1000)
    return () => clearInterval(t)
  }, [writer, screen, send])

  // Fiyat değişince hemen gönder.
  useEffect(() => { if (writer && message) void send('fiyat değişti') }, [message, writer, send])

  // Ekran uykuya geçip gönderim durmasın.
  useEffect(() => {
    if (!writer || !nav.wakeLock) return
    let lock: { release(): Promise<void> } | null = null
    nav.wakeLock.request('screen').then((l) => { lock = l }).catch(() => {})
    return () => { void lock?.release() }
  }, [writer])

  async function connectSerial() {
    if (!screen) return
    if (!nav.serial) { add('Bu tarayıcı Web Serial desteklemiyor. Chrome veya Edge kullanın.'); return }
    setBusy(true)
    try {
      const port = await nav.serial.requestPort()
      const s = screen.serial
      await port.open({ baudRate: s.baudRate, dataBits: s.dataBits, stopBits: s.stopBits, parity: s.parity })
      const w = port.writable!.getWriter()
      setWriter({
        kind: 'COM',
        label: `COM ${s.baudRate} ${s.dataBits}${s.parity[0].toUpperCase()}${s.stopBits}`,
        write: (b) => w.write(b),
        close: async () => { w.releaseLock(); await port.close() },
      })
      add(`COM port açıldı (${s.baudRate} baud).`)
    } catch (e) {
      add(`COM bağlanamadı: ${(e as Error).message}`)
    } finally {
      setBusy(false)
    }
  }

  async function connectBluetooth() {
    if (!screen) return
    if (!nav.bluetooth) { add('Bu tarayıcı Web Bluetooth desteklemiyor. Chrome veya Edge kullanın.'); return }
    setBusy(true)
    try {
      const b = screen.bluetooth
      const device = await nav.bluetooth.requestDevice({ acceptAllDevices: true, optionalServices: [b.serviceUuid] })
      const server = await device.gatt!.connect()
      const ch = await (await server.getPrimaryService(b.serviceUuid)).getCharacteristic(b.characteristicUuid)
      setWriter({
        kind: 'Bluetooth',
        label: device.name ?? 'BLE cihaz',
        // BLE tek yazımda sınırlı bayt alır: parçalara bölünür.
        write: async (bytes) => {
          for (let i = 0; i < bytes.length; i += b.chunkSize) {
            const part = bytes.slice(i, i + b.chunkSize)
            await (ch.writeValueWithoutResponse ? ch.writeValueWithoutResponse(part) : ch.writeValue(part))
          }
        },
        close: async () => server.disconnect?.(),
      })
      add(`Bluetooth bağlandı: ${device.name ?? 'adsız cihaz'}.`)
    } catch (e) {
      add(`Bluetooth bağlanamadı: ${(e as Error).message}`)
    } finally {
      setBusy(false)
    }
  }

  async function disconnect() {
    try { await writer?.close() } catch { /* zaten kapalı */ }
    setWriter(null)
    add('Bağlantı kapatıldı.')
  }

  if (!screen) return <div className="sender"><div className="sender-card">{error ?? 'Yükleniyor…'}</div></div>

  const secure = window.isSecureContext

  return (
    <div className="sender">
      <div className="sender-card">
        <h1>{screen.name} — Gönderici</h1>
        <p className="muted-light">
          Bu sayfa açık kaldığı sürece fiyatlar {screen.serial.intervalSeconds} sn'de bir ve her fiyat değişiminde ekrana gönderilir.
          Chrome veya Edge ile açın; bilgisayar uykuya geçmemeli.
        </p>
        {!secure && <div className="alert error">Web Serial / Bluetooth yalnızca https veya localhost adresinde çalışır.</div>}

        <div className="sender-actions">
          {!writer ? (
            <>
              <button className="hero-btn" disabled={busy} onClick={connectSerial}>COM porta bağlan</button>
              <button className="hero-btn ghost" disabled={busy} onClick={connectBluetooth}>Bluetooth (BLE) cihaza bağlan</button>
            </>
          ) : (
            <>
              <span className="sender-status">● Bağlı: {writer.kind} · {writer.label}</span>
              <button className="hero-btn" onClick={() => { lastSent.current = 0; void send('elle') }}>Şimdi gönder</button>
              <button className="hero-btn ghost" onClick={disconnect}>Bağlantıyı kes</button>
            </>
          )}
        </div>
        <p className="muted-light small">
          Klasik Bluetooth (SPP) LED kartları Windows'ta eşleştirilince sanal COM port olarak görünür: "COM porta bağlan" ile seçin.
        </p>

        <h2>Son gönderilen</h2>
        <pre className="sender-preview">{preview || '—'}</pre>
        <h2>Günlük</h2>
        <pre className="sender-log">{log.join('\n') || '—'}</pre>
      </div>
    </div>
  )
}
