import type { SocialType } from '../api'

// Material "phone" ahizesi (24×24).
const PHONE = 'M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z'

/** Sade, tek renkli sosyal medya simgeleri (currentColor). */
export function SocialIcon({ type, size = 26 }: { type: SocialType; size?: number }) {
  const common = { width: size, height: size, viewBox: '0 0 24 24', 'aria-hidden': true as const }
  switch (type) {
    case 'WhatsApp':
      return (
        <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round">
          <path d="M12 3a9 9 0 0 0-7.8 13.5L3 21l4.6-1.2A9 9 0 1 0 12 3z" />
          <path d={PHONE} fill="currentColor" stroke="none" transform="translate(6.6 6.6) scale(.45)" />
        </svg>
      )
    case 'Instagram':
      return (
        <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.9">
          <rect x="3.5" y="3.5" width="17" height="17" rx="5" />
          <circle cx="12" cy="12" r="4" />
          <circle cx="17.2" cy="6.8" r="1.1" fill="currentColor" stroke="none" />
        </svg>
      )
    case 'Facebook':
      return (
        <svg {...common} fill="currentColor">
          <path d="M13.5 21v-7.5h2.6l.4-3h-3V8.6c0-.9.3-1.5 1.6-1.5h1.6V4.4c-.3 0-1.2-.1-2.3-.1-2.3 0-3.9 1.4-3.9 4v2.2H8v3h2.5V21h3z" />
        </svg>
      )
    case 'YouTube':
      return (
        <svg {...common} fill="currentColor">
          <path d="M21.6 7.2a2.6 2.6 0 0 0-1.8-1.8C18.2 5 12 5 12 5s-6.2 0-7.8.4A2.6 2.6 0 0 0 2.4 7.2 27 27 0 0 0 2 12a27 27 0 0 0 .4 4.8 2.6 2.6 0 0 0 1.8 1.8C5.8 19 12 19 12 19s6.2 0 7.8-.4a2.6 2.6 0 0 0 1.8-1.8A27 27 0 0 0 22 12a27 27 0 0 0-.4-4.8zM10 15V9l5.2 3L10 15z" />
        </svg>
      )
    case 'Telegram':
      return (
        <svg {...common} fill="currentColor">
          <path d="M21.4 4.1 2.9 11.2c-1 .4-1 1.6 0 1.9l4.6 1.5 1.8 5.5c.2.7 1.1.9 1.6.4l2.6-2.4 4.8 3.5c.6.4 1.4.1 1.6-.6l3-15.4c.2-.9-.7-1.6-1.5-1.5zM9.6 14.4l-.4 3.5-1.3-3.9 9.6-6.4-7.9 6.8z" />
        </svg>
      )
    case 'Phone':
      return (
        <svg {...common} fill="currentColor">
          <path d={PHONE} />
        </svg>
      )
  }
}

export const socialLabels: Record<SocialType, string> = {
  WhatsApp: 'WhatsApp',
  Instagram: 'Instagram',
  Facebook: 'Facebook',
  YouTube: 'YouTube',
  Telegram: 'Telegram',
  Phone: 'Telefon',
}
