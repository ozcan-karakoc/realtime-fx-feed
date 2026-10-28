import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import type { PublicPriceItem } from '../api'
import { initials, money } from '../format'
import { useLive } from '../useLivePrices'

type Side = 'Buy' | 'Sell'

interface LockConfig { enabled: boolean; minutes: number; maxQuantity: number }

interface PublicLock {
  ref: string
  productCode: string
  productName: string
  side: Side
  unitPrice: number
  quantity: number
  total: number
  decimals: number
  createdUtc: string
  expiresUtc: string
  status: 'Active' | 'Expired' | 'Completed' | 'Cancelled'
  customer: string
}

interface Msg { id: number; from: 'bot' | 'user'; body: ReactNode }

type Step =
  | { kind: 'menu' }
  | { kind: 'pick'; purpose: 'price' | 'lock' }
  | { kind: 'side'; item: PublicPriceItem }
  | { kind: 'form'; item: PublicPriceItem; side: Side }
  | { kind: 'ref' }

const statusLabels: Record<PublicLock['status'], string> = {
  Active: 'Geçerli',
  Expired: 'Süresi doldu',
  Completed: 'İşlem tamamlandı',
  Cancelled: 'İptal edildi',
}

const norm = (s: string) => s.toLocaleLowerCase('tr-TR').replace(/[^a-zçğıöşü0-9]/g, '')

let nextId = 1

/** Sağ altta açılan asistan: fiyat sorma, kur sabitleme (süre panelden), referans sorgulama. */
export function ChatBot() {
  const { message, content } = useLive()
  const items = message?.items ?? []
  const [open, setOpen] = useState(false)
  const [config, setConfig] = useState<LockConfig | null>(null)
  const [msgs, setMsgs] = useState<Msg[]>([])
  const [step, setStep] = useState<Step>({ kind: 'menu' })
  const [text, setText] = useState('')
  const listRef = useRef<HTMLDivElement>(null)

  const say = (from: Msg['from'], body: ReactNode) => setMsgs((m) => [...m, { id: nextId++, from, body }])

  useEffect(() => {
    if (!open || config) return
    fetch('/api/v1/locks/config', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((c: LockConfig | null) => setConfig(c ?? { enabled: false, minutes: 0, maxQuantity: 0 }))
      .catch(() => setConfig({ enabled: false, minutes: 0, maxQuantity: 0 }))
  }, [open, config])

  useEffect(() => {
    if (open && msgs.length === 0)
      say('bot', <>Merhaba, {content?.shopName ?? 'DEMO'} asistanı. Size nasıl yardımcı olabilirim?</>)
  }, [open, msgs.length, content?.shopName])

  useEffect(() => { listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: 'smooth' }) }, [msgs, step])

  function showPrice(item: PublicPriceItem) {
    say('bot', (
      <div className="bot-price">
        <strong>{item.name}</strong>
        <span>Alış <b>{money(item.buy, item.decimals)}</b></span>
        <span>Satış <b>{money(item.sell, item.decimals)}</b></span>
      </div>
    ))
  }

  function pick(item: PublicPriceItem, purpose: 'price' | 'lock') {
    say('user', item.name)
    if (purpose === 'price') {
      showPrice(item)
      setStep({ kind: 'menu' })
    } else {
      say('bot', `${item.name}: almak mı istiyorsunuz, satmak mı?`)
      setStep({ kind: 'side', item })
    }
  }

  async function lookup(reference: string) {
    say('user', reference)
    try {
      const r = await fetch(`/api/v1/locks/${encodeURIComponent(reference.trim())}`, { cache: 'no-store' })
      if (!r.ok) { say('bot', 'Bu referans numarası bulunamadı.'); return }
      say('bot', <LockCard lock={(await r.json()) as PublicLock} />)
    } catch {
      say('bot', 'Bağlantı hatası, lütfen tekrar deneyin.')
    } finally {
      setStep({ kind: 'menu' })
    }
  }

  function onText(e: FormEvent) {
    e.preventDefault()
    const t = text.trim()
    if (!t) return
    setText('')
    if (step.kind === 'ref' || /^od-\d{6}-[a-z0-9]{5}$/i.test(t)) { void lookup(t); return }
    say('user', t)
    const q = norm(t)
    const found = items.filter((i) => norm(i.name).includes(q) || norm(i.code).includes(q) || q.includes(norm(i.code)))
    if (found.length === 1) showPrice(found[0])
    else if (found.length > 1) { say('bot', 'Birden fazla ürün buldum, hangisi?'); setStep({ kind: 'pick', purpose: 'price' }) }
    else say('bot', 'Anlayamadım. Aşağıdaki seçeneklerden birini kullanabilirsiniz.')
  }

  const lockEnabled = config?.enabled ?? false

  return (
    <>
      <button className={`bot-fab ${open ? 'open' : ''}`} onClick={() => setOpen((v) => !v)} aria-label={open ? 'Asistanı kapat' : 'Asistanı aç'}>
        {open ? '✕' : '💬'}
      </button>
      <aside className={`bot-panel ${open ? 'open' : ''}`} aria-hidden={!open} aria-label="Asistan">
        <header className="bot-head">
          <span className="brand-mark small">{initials(content?.shopName ?? 'DEMO')}</span>
          <div>
            <strong>Asistan</strong>
            <span>Fiyat · Kur sabitleme · Referans</span>
          </div>
          <button className="bot-close" onClick={() => setOpen(false)} aria-label="Kapat">✕</button>
        </header>

        <div className="bot-list" ref={listRef}>
          {msgs.map((m) => <div key={m.id} className={`bot-msg ${m.from}`}>{m.body}</div>)}

          <div className="bot-actions">
            {step.kind === 'menu' && (
              <>
                <button onClick={() => { say('user', 'Fiyat sormak istiyorum'); say('bot', 'Hangi ürün?'); setStep({ kind: 'pick', purpose: 'price' }) }}>Fiyat sor</button>
                {lockEnabled && (
                  <button onClick={() => { say('user', 'Kur sabitlemek istiyorum'); say('bot', `Fiyatı ${config!.minutes} dakika sabitleyebilirim. Hangi ürün?`); setStep({ kind: 'pick', purpose: 'lock' }) }}>
                    Kur sabitle ({config!.minutes} dk)
                  </button>
                )}
                <button onClick={() => { say('user', 'Referans sorgulamak istiyorum'); say('bot', 'Referans numaranızı yazın (ör. OD-261028-ABCDE).'); setStep({ kind: 'ref' }) }}>Referans sorgula</button>
              </>
            )}
            {step.kind === 'pick' && (
              <>
                {items.map((i) => <button key={i.code} onClick={() => pick(i, step.purpose)}>{i.name}</button>)}
                {items.length === 0 && <span className="muted-light">Fiyatlar yükleniyor…</span>}
                <button className="ghost" onClick={() => setStep({ kind: 'menu' })}>← Geri</button>
              </>
            )}
            {step.kind === 'side' && (
              <>
                <button onClick={() => { say('user', 'Almak istiyorum'); setStep({ kind: 'form', item: step.item, side: 'Buy' }) }}>
                  Almak istiyorum · {money(step.item.sell, step.item.decimals)}
                </button>
                <button onClick={() => { say('user', 'Satmak istiyorum'); setStep({ kind: 'form', item: step.item, side: 'Sell' }) }}>
                  Satmak istiyorum · {money(step.item.buy, step.item.decimals)}
                </button>
                <button className="ghost" onClick={() => setStep({ kind: 'menu' })}>← Vazgeç</button>
              </>
            )}
          </div>

          {step.kind === 'form' && config && (
            <LockForm
              item={items.find((i) => i.code === step.item.code) ?? step.item}
              side={step.side}
              config={config}
              onCancel={() => setStep({ kind: 'menu' })}
              onDone={(lock) => {
                say('bot', <>Fiyatınız sabitlendi. Referans numaranızı saklayın ve süre dolmadan dükkanımıza başvurun.</>)
                say('bot', <LockCard lock={lock} />)
                setStep({ kind: 'menu' })
              }}
              onError={(t) => say('bot', t)}
            />
          )}
        </div>

        <form className="bot-input" onSubmit={onText}>
          <input value={text} onChange={(e) => setText(e.target.value)} placeholder={step.kind === 'ref' ? 'OD-261028-ABCDE' : 'Ürün adı yazın (ör. çeyrek, dolar)'} maxLength={60} />
          <button aria-label="Gönder">➤</button>
        </form>
      </aside>
    </>
  )
}

function LockForm({ item, side, config, onCancel, onDone, onError }: {
  item: PublicPriceItem
  side: Side
  config: LockConfig
  onCancel: () => void
  onDone: (l: PublicLock) => void
  onError: (t: string) => void
}) {
  const [quantity, setQuantity] = useState('1')
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [busy, setBusy] = useState(false)
  const unit = side === 'Buy' ? item.sell : item.buy
  const qty = Number(quantity.replace(',', '.'))
  const valid = Number.isFinite(qty) && qty > 0 && qty <= config.maxQuantity

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!valid) return
    setBusy(true)
    try {
      const r = await fetch('/api/v1/locks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ productCode: item.code, side, quantity: qty, name, phone }),
      })
      if (r.status === 429) { onError('Çok fazla deneme yaptınız. Lütfen birkaç dakika sonra tekrar deneyin.'); return }
      const body = await r.json().catch(() => null)
      if (!r.ok) { onError(body?.title ?? 'Sabitleme yapılamadı.'); return }
      onDone(body as PublicLock)
    } catch {
      onError('Bağlantı hatası, lütfen tekrar deneyin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="bot-form" onSubmit={submit}>
      <div className="bot-form-title">{item.name} · {side === 'Buy' ? 'Alış talebi' : 'Satış talebi'}</div>
      <label>Miktar<input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} required /></label>
      <label>Ad soyad<input value={name} onChange={(e) => setName(e.target.value)} minLength={2} maxLength={80} required autoComplete="name" /></label>
      <label>Telefon<input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} required autoComplete="tel" placeholder="05xx xxx xx xx" /></label>
      <div className="bot-total">
        <span>Birim {money(unit, item.decimals)}</span>
        <strong>{valid ? money(unit * qty, 2) : '—'} ₺</strong>
      </div>
      {!valid && <div className="bot-hint">Miktar 0 ile {config.maxQuantity} arasında olmalı.</div>}
      <p className="bot-hint">Fiyat sunucuda o anki fiyattan {config.minutes} dakika sabitlenir.</p>
      <div className="bot-form-actions">
        <button type="button" className="ghost" onClick={onCancel}>Vazgeç</button>
        <button disabled={busy || !valid}>{busy ? 'Sabitleniyor…' : `${config.minutes} dk sabitle`}</button>
      </div>
    </form>
  )
}

function LockCard({ lock }: { lock: PublicLock }) {
  const [now, setNow] = useState(() => Date.now())
  const expires = new Date(lock.expiresUtc).getTime()
  const live = lock.status === 'Active' && now < expires
  useEffect(() => {
    if (!live) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [live])
  const left = Math.max(0, Math.floor((expires - now) / 1000))
  const status = lock.status === 'Active' && !live ? 'Expired' : lock.status

  return (
    <div className={`lock-card ${status.toLowerCase()}`}>
      <div className="lock-ref">{lock.ref}</div>
      <div>{lock.productName} · {lock.side === 'Buy' ? 'Alış' : 'Satış'} · miktar {lock.quantity}</div>
      <div>Birim <b>{money(lock.unitPrice, lock.decimals)}</b> · Toplam <b>{money(lock.total, 2)} ₺</b></div>
      <div className="lock-status">
        {statusLabels[status]}
        {live && <> · kalan <b className="mono">{String(Math.floor(left / 60)).padStart(2, '0')}:{String(left % 60).padStart(2, '0')}</b></>}
      </div>
    </div>
  )
}
