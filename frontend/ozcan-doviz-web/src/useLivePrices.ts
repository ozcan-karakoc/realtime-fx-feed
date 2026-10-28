import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { createContext, createElement, useContext, useEffect, useState, type ReactNode } from 'react'
import type { PublicContent, PublicPriceMessage } from './api'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

interface Live {
  message: PublicPriceMessage | null
  state: LiveState
  content: PublicContent | null
}

const LiveContext = createContext<Live>({ message: null, state: 'connecting', content: null })

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
 * Sunucu "prices" ile fiyatı, "content" ile içerik değişimini bildirir; içerik o an yeniden çekilir.
 */
export function LiveProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<PublicPriceMessage | null>(null)
  const [state, setState] = useState<LiveState>('connecting')
  const [content, setContent] = useState<PublicContent | null>(null)

  useEffect(() => {
    let cancelled = false
    const refresh = () => loadContent().then((c) => { if (!cancelled && c) setContent(c) })
    void refresh()

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/prices')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('prices', (m: PublicPriceMessage) => setMessage(m))
    connection.on('content', () => void refresh())
    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => { setState('live'); void refresh() })
    connection.onclose(() => setState('offline'))

    const start = async () => {
      try {
        await connection.start()
        if (!cancelled) setState('live')
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
      void connection.stop()
    }
  }, [])

  return createElement(LiveContext.Provider, { value: { message, state, content } }, children)
}

export const useLive = () => useContext(LiveContext)
