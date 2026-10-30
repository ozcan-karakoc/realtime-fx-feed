import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { createContext, createElement, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { PublicContent, PublicNotice, PublicPriceMessage } from './api'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

/** Sunucunun "lock" olayı: botta yapılan sabitlemenin yeni durumu. */
export interface LockUpdate {
  ref: string
  productName: string
  status: 'Active' | 'Expired' | 'Completed' | 'Cancelled'
  [key: string]: unknown
}

interface Live {
  message: PublicPriceMessage | null
  state: LiveState
  content: PublicContent | null
  /** Bir sabitleme referansını izle; panelde kapatılınca "lock" olayı gelir. Yeniden bağlanınca tekrar abone olunur. */
  watchLock: (reference: string) => void
  /** Durum değişikliklerini dinle; dönen fonksiyon dinlemeyi bırakır. */
  onLock: (handler: (u: LockUpdate) => void) => () => void
  /** Panelden gönderilen bildirimler ('notice') ve geri çekilenler ('removed', id). */
  onNotice: (handler: (e: { type: 'notice'; notice: PublicNotice } | { type: 'removed'; id: string }) => void) => () => void
}

const LiveContext = createContext<Live>({
  message: null, state: 'connecting', content: null, watchLock: () => {}, onLock: () => () => {}, onNotice: () => () => {},
})

async function loadContent(): Promise<PublicContent | null> {
  try {
    // İçerik değişince sunucu "content" olayı gönderir; önbellekten eski kopya okunmasın.
    const r = await fetch('/api/v1/content', { cache: 'no-store' })
    return r.ok ? ((await r.json()) as PublicContent) : null
  } catch {
    return null
  }
}

/**
 * Sitenin tek SignalR bağlantısı (/hubs/prices). Sayfalar arası gezinmede kopmaz.
 * Sunucu "prices" ile fiyatı, "content" ile içerik değişimini, "lock" ile izlenen sabitlemenin sonucunu bildirir.
 */
export function LiveProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<PublicPriceMessage | null>(null)
  const [state, setState] = useState<LiveState>('connecting')
  const [content, setContent] = useState<PublicContent | null>(null)
  const connectionRef = useRef<HubConnection | null>(null)
  const watched = useRef(new Set<string>())
  const lockHandlers = useRef(new Set<(u: LockUpdate) => void>())
  type NoticeEvent = { type: 'notice'; notice: PublicNotice } | { type: 'removed'; id: string }
  const noticeHandlers = useRef(new Set<(e: NoticeEvent) => void>())

  const subscribe = (c: HubConnection, reference: string) => {
    if (c.state === HubConnectionState.Connected) c.invoke('WatchLock', reference).catch(() => {})
  }

  useEffect(() => {
    let cancelled = false
    const refresh = () => loadContent().then((c) => { if (!cancelled && c) setContent(c) })
    void refresh()

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/prices')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connectionRef.current = connection

    const resubscribe = () => watched.current.forEach((r) => subscribe(connection, r))

    connection.on('prices', (m: PublicPriceMessage) => setMessage(m))
    connection.on('content', () => void refresh())
    connection.on('lock', (u: LockUpdate) => lockHandlers.current.forEach((h) => h(u)))
    connection.on('notice', (n: PublicNotice) => noticeHandlers.current.forEach((h) => h({ type: 'notice', notice: n })))
    connection.on('notice-removed', (id: string) => noticeHandlers.current.forEach((h) => h({ type: 'removed', id })))
    connection.onreconnecting(() => setState('reconnecting'))
    // Yeni bağlantı kimliği gruplarda değildir: izlenen referanslara yeniden abone olunur.
    connection.onreconnected(() => { setState('live'); void refresh(); resubscribe() })
    connection.onclose(() => setState('offline'))

    const start = async () => {
      try {
        await connection.start()
        if (cancelled) return
        setState('live')
        resubscribe()
      } catch {
        if (cancelled) return
        setState('offline')
        setTimeout(() => {
          if (!cancelled && connection.state === HubConnectionState.Disconnected) void start()
        }, 5000)
      }
    }
    void start()

    return () => {
      cancelled = true
      connectionRef.current = null
      void connection.stop()
    }
  }, [])

  const watchLock = useCallback((reference: string) => {
    const r = reference.trim().toUpperCase()
    if (watched.current.has(r)) return
    watched.current.add(r)
    if (connectionRef.current) subscribe(connectionRef.current, r)
  }, [])

  const onLock = useCallback((handler: (u: LockUpdate) => void) => {
    lockHandlers.current.add(handler)
    return () => { lockHandlers.current.delete(handler) }
  }, [])

  const onNotice = useCallback((handler: (e: NoticeEvent) => void) => {
    noticeHandlers.current.add(handler)
    return () => { noticeHandlers.current.delete(handler) }
  }, [])

  const value = useMemo(() => ({ message, state, content, watchLock, onLock, onNotice }), [message, state, content, watchLock, onLock, onNotice])
  return createElement(LiveContext.Provider, { value }, children)
}

export const useLive = () => useContext(LiveContext)
