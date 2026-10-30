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
  provider: string
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
  provider?: string | null
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
  categoryProviders: Record<string, string>
  rateLock: RateLockSettings
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

export interface ContactSection {
  phone?: string | null
  whatsApp?: string | null
  email?: string | null
  address?: string | null
  workingHours?: string | null
  mapEmbedUrl?: string | null
  imageUrl?: string | null
  locations: BranchLocation[]
}

export interface PublicContent {
  shopName: string
  brokerId: string
  updatedUtc: string
  ticker: { enabled: boolean; durationSeconds: number; showPrices: boolean; items: string[] }
  slides: { id: string; imageUrl: string; title: string; subtitle?: string | null }[]
  about: { title: string; body: string; imageUrl?: string | null }
  contact: ContactSection
  branding: Branding
  social: { type: SocialType; href: string; label: string; floating: boolean }[]
  home: HomeSection
  instagram: InstagramSection & { profileUrl?: string | null }
  locations: PublicLocation[]
  pages: PagesSection
}

export interface Slide {
  id: string
  imageUrl: string
  title: string
  subtitle?: string | null
  enabled: boolean
  order: number
}

export interface SiteContent {
  brokerId: string
  updatedUtc: string
  updatedBy?: string | null
  ticker: { enabled: boolean; durationSeconds: number; showPrices: boolean; items: { text: string; enabled: boolean }[] }
  slides: Slide[]
  about: { title: string; body: string; imageUrl?: string | null }
  contact: ContactSection
  branding: Branding
  social: SocialLink[]
  home: HomeSection
  instagram: InstagramSection
  pages: PagesSection
}

export interface MediaFile {
  name: string
  url: string
  size: number
  createdUtc: string
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
  // FormData (fotoğraf yükleme) kendi multipart başlığını koyar.
  if (typeof init.body === 'string' && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
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

export interface RateLockSettings {
  enabled: boolean
  minutes: number
  maxQuantity: number
}

export interface RateLock {
  ref: string
  productCode: string
  productName: string
  side: 'Buy' | 'Sell'
  unitPrice: number
  quantity: number
  total: number
  decimals: number
  customerName: string
  customerPhone: string
  note?: string | null
  channel: string
  createdUtc: string
  expiresUtc: string
  closedStatus?: string | null
  closedUtc?: string | null
  closedBy?: string | null
}

export interface AdminLock {
  lock: RateLock
  status: 'Active' | 'Expired' | 'Completed' | 'Cancelled'
}

export type SocialType = 'WhatsApp' | 'Instagram' | 'Facebook' | 'YouTube' | 'Telegram' | 'Phone'

export interface SocialLink {
  type: SocialType
  value: string
  floating: boolean
  message?: string | null
}

export interface Branding {
  headerLogoUrl?: string | null
  footerImageUrl?: string | null
}

export interface HomeSection {
  introTitle: string
  introText: string
  features: { icon: string; title: string; text: string }[]
  stats: { value: string; label: string }[]
  ctaTitle: string
  ctaText: string
  introImageUrl?: string | null
}

export interface InstagramSection {
  enabled: boolean
  title: string
  count: number
}

export interface InstagramPost {
  id: string
  caption?: string | null
  mediaType: string
  imageUrl: string
  permalink: string
  timestamp: string
}

export interface InstagramStatus {
  hasToken: boolean
  tokenHint?: string | null
  tokenUpdatedUtc?: string | null
  userName?: string | null
  lastFetchUtc?: string | null
  lastError?: string | null
}

export type DisplayKind = 'Tv' | 'Pano' | 'Led'
export type DisplayOutput = 'Hdmi' | 'Serial' | 'Bluetooth'

export interface DisplayScreen {
  id: string
  name: string
  kind: DisplayKind
  output: DisplayOutput
  enabled: boolean
  updatedUtc?: string
  layout: {
    title: string
    theme: 'dark' | 'light' | 'gold'
    orientation: 'landscape' | 'portrait'
    columns: number
    fontScale: number
    showLogo: boolean
    showClock: boolean
    showTicker: boolean
    showBuy: boolean
    showSell: boolean
    productCodes: string[]
    pageSize: number
    pageSeconds: number
  }
  serial: {
    baudRate: number
    dataBits: number
    parity: 'none' | 'even' | 'odd'
    stopBits: number
    encoding: string
    asciiFold: boolean
    upperCase: boolean
    headerTemplate: string
    lineTemplate: string
    lineSeparator: string
    prefixHex: string
    suffixHex: string
    numberCulture: 'tr' | 'invariant'
    maxLineLength: number
    intervalSeconds: number
  }
  bluetooth: { serviceUuid: string; characteristicUuid: string; chunkSize: number }
}

export interface FramePreview { text: string; hex: string; length: number }

export interface CustomProvider {
  key: string
  name: string
  url: string
  enabled: boolean
  protocol: 'auto' | 'raw' | 'socketio'
  subscribeMessage?: string | null
  origin?: string | null
  dataPath: string
  symbolField: string
  buyField: string
  sellField: string
  nameField?: string | null
  aliases?: string | null
}

export interface CustomProviderTest { ok: boolean; message: string; sample?: string | null; symbols: string[] }

export interface BranchLocation {
  name: string
  address?: string | null
  mapQuery: string
  phone?: string | null
  workingHours?: string | null
}

export interface PublicLocation {
  name: string
  address?: string | null
  phone?: string | null
  workingHours?: string | null
  embedUrl: string
  directionsUrl: string
}

export interface PagesSection {
  history: boolean
  historyTitle: string
  calculator: boolean
}

export interface HistoryCode { code: string; name: string; group: 'altin' | 'doviz'; decimals: number }
export interface HistoryPoint { date: string; buy: number; sell: number }
export interface HistorySeries { code: string; name: string; decimals: number; points: HistoryPoint[]; missingDays: number }

export interface PublicNotice {
  id: string
  title: string
  message: string
  level: 'info' | 'success' | 'warning' | 'danger'
  linkUrl?: string | null
  linkText?: string | null
  createdUtc: string
}

export interface SiteNotice extends PublicNotice {
  displayMinutes: number
  createdBy?: string | null
  withdrawnUtc?: string | null
}
