import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useState } from 'react'
import type { PublicPriceMessage } from './api'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

/** /hubs/prices SignalR akışı. Bağlanınca sunucu son tabloyu hemen gönderir. */
export function useLivePrices() {
  const [message, setMessage] = useState<PublicPriceMessage | null>(null)
  const [state, setState] = useState<LiveState>('connecting')

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/prices')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('prices', (m: PublicPriceMessage) => setMessage(m))
    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => setState('live'))
    connection.onclose(() => setState('offline'))

    let cancelled = false
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

  return { message, state }
}
