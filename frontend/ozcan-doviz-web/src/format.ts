const cache = new Map<number, Intl.NumberFormat>()

export function money(value: number, decimals = 2) {
  let f = cache.get(decimals)
  if (!f) {
    f = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
    cache.set(decimals, f)
  }
  return f.format(value)
}

// Sunucudaki *Utc alanları SQL'den saat dilimi eki olmadan gelebilir; UTC kabul edilir.
const asUtc = (iso: string) => (/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : iso + 'Z')

export function dateTime(iso?: string | null) {
  if (!iso) return '—'
  const d = new Date(asUtc(iso))
  if (Number.isNaN(d.getTime()) || d.getFullYear() < 2000) return '—'
  return d.toLocaleString('tr-TR')
}

export function time(iso?: string | null) {
  if (!iso) return '—'
  const d = new Date(asUtc(iso))
  if (Number.isNaN(d.getTime()) || d.getFullYear() < 2000) return '—'
  return d.toLocaleTimeString('tr-TR')
}

export const categoryLabels: Record<string, string> = {
  altin: 'Altın',
  doviz: 'Döviz',
  parite: 'Parite',
  maden: 'Değerli Maden',
}
