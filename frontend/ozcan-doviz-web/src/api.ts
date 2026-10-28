// Ortak tipler ve fetch sarmalayıcısı. Yönetici JWT'si localStorage'da tutulur.

export type Direction = 'up' | 'down' | 'same'

export interface PublicPriceItem {
  code: string
  name: string
  category: string
  buy: number
  sell: number
  decimals: number
  direction: Direction
}

export interface PublicPriceMessage {
  type: 'prices'
  provider: string
  updatedUtc: string
  stale: boolean
  items: PublicPriceItem[]
}

export interface BoardItem extends PublicPriceItem {
  sourceBuy: number
  sourceSell: number
  sourceSymbol: string
}

export type MarginMode = 'Add' | 'Percent' | 'Multiply'

export interface MarginSide {
  mode: MarginMode
  value: number
}

export interface ProductRule {
  code: string
  name: string
  category: string
  sourceSymbol: string
  buy: MarginSide
  sell: MarginSide
  decimals: number
  visible: boolean
  order: number
}

export interface MarginDocument {
  version: number
  updatedUtc: string
  updatedBy?: string | null
  products: ProductRule[]
}

export interface FeedSettings {
  activeProvider: string
  fallbackProvider?: string | null
  pollSeconds: number
  shopName: string
}

export interface ProviderInfo {
  key: string
  displayName: string
  description: string
  transport: string
}

export interface RawSymbol {
  symbol: string
  name?: string | null
  buy: number
  sell: number
}

export interface ClientView {
  id: number
  name: string
  keyPrefix: string
  allowedCodes?: string | null
  isActive: boolean
  createdUtc: string
  lastSeenUtc?: string | null
}

export interface ClientCreated {
  client: ClientView
  apiKey: string
  webSocketUrl: string
  restUrl: string
}

export interface HaremRate {
  code: string
  buy: number
  sell: number
  high: number
  low: number
  recordDate: string
}

export interface UserView {
  id: number
  userName: string
  role: string
  isActive: boolean
  createdUtc: string
  lastLoginUtc?: string | null
}

export interface AuditEntry {
  id: number
  timeUtc: string
  user: string
  action: string
  detail?: string | null
}

const TOKEN_KEY = 'ozcan.token'
const USER_KEY = 'ozcan.user'

export const auth = {
  get token() {
    try { return localStorage.getItem(TOKEN_KEY) } catch { return null }
  },
  get user() {
    try { return localStorage.getItem(USER_KEY) } catch { return null }
  },
  set(token: string, user: string) {
    try {
      localStorage.setItem(TOKEN_KEY, token)
      localStorage.setItem(USER_KEY, user)
    } catch { /* gizli pencere */ }
  },
  clear() {
    try {
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
    } catch { /* gizli pencere */ }
  },
}

export class ApiError extends Error {
  status: number
  details: string[]
  constructor(status: number, message: string, details: string[] = []) {
    super(message)
    this.status = status
    this.details = details
  }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const token = auth.token
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const res = await fetch(path, { ...init, headers })
  if (res.status === 401 && path.startsWith('/api/v1/admin')) {
    auth.clear()
    window.location.assign('/admin/login')
  }
  if (!res.ok) {
    let title = `${res.status} ${res.statusText}`
    let details: string[] = []
    try {
      const body = await res.json()
      title = body.title ?? title
      if (body.errors) details = Object.values(body.errors as Record<string, string[]>).flat()
    } catch { /* gövde JSON değil */ }
    throw new ApiError(res.status, title, details)
  }
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export const json = (body: unknown) => JSON.stringify(body)
