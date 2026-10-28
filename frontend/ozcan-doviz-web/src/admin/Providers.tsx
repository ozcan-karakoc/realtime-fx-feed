import { useEffect, useState } from 'react'
import { api, json, type FeedSettings, type ProviderInfo } from '../api'
import { categoryLabels } from '../format'
import { CustomProviders } from './CustomProviders'

interface TestResult { ok: boolean; message: string; symbolCount: number }

export function Providers() {
  const [providers, setProviders] = useState<ProviderInfo[]>([])
  const [settings, setSettings] = useState<FeedSettings | null>(null)
  const [tests, setTests] = useState<Record<string, TestResult | 'running'>>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  useEffect(() => {
    api<{ settings: FeedSettings; providers: ProviderInfo[] }>('/api/v1/admin/providers').then((r) => {
      setProviders(r.providers)
      setSettings(r.settings)
    })
  }, [])

  // Özel sağlayıcı eklenince/silinince yalnızca liste tazelenir; formdaki kaydedilmemiş ayarlar korunur.
  const reloadProviders = () =>
    api<{ providers: ProviderInfo[] }>('/api/v1/admin/providers').then((r) => setProviders(r.providers))

  async function test(key: string) {
    setTests((t) => ({ ...t, [key]: 'running' }))
    try {
      const r = await api<TestResult>(`/api/v1/admin/providers/${key}/test`, { method: 'POST' })
      setTests((t) => ({ ...t, [key]: r }))
    } catch (e) {
      setTests((t) => ({ ...t, [key]: { ok: false, message: (e as Error).message, symbolCount: 0 } }))
    }
  }

  async function save() {
    if (!settings) return
    setMessage(null)
    try {
      const saved = await api<FeedSettings>('/api/v1/admin/providers', { method: 'PUT', body: json(settings) })
      setSettings(saved)
      setMessage({ ok: true, text: 'Kaydedildi. Fiyat akışı yeni sağlayıcıya geçti.' })
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message })
    }
  }

  if (!settings) return <div className="page"><div className="panel pad">Yükleniyor…</div></div>

  return (
    <div className="page">
      <section className="panel form-panel">
        <div className="form-title">Fiyat Sağlayıcıları</div>
        <div className="pad">
          <p className="muted">
            Etkin sağlayıcı fiyatların kaynağıdır; hata verirse yedek sağlayıcıya geçilir. Marj kuralları ortak sembollere
            (HASALTIN, CEYREK, USDTRY…) bağlı olduğundan sağlayıcı değişince marjlar korunur.
          </p>

          <div className="provider-grid">
            {providers.map((p) => {
              const t = tests[p.key]
              const active = settings.activeProvider === p.key
              return (
                <div key={p.key} className={`provider-card ${active ? 'active' : ''}`}>
                  <label className="provider-pick">
                    <input type="radio" name="active" checked={active} onChange={() => setSettings({ ...settings, activeProvider: p.key })} />
                    <span>
                      <strong>{p.displayName}</strong>
                      {active && <span className="badge primary">Genel</span>}
                      <span className={`badge ${p.transport === 'Socket' ? 'ok' : ''}`}>{p.transport === 'Socket' ? 'WebSocket' : 'HTTP'}</span>
                    </span>
                  </label>
                  <p className="muted small">{p.description}</p>
                  <div className="provider-foot">
                    <button className="btn small" onClick={() => test(p.key)} disabled={t === 'running'}>
                      {t === 'running' ? 'Deneniyor…' : 'Bağlantıyı dene'}
                    </button>
                    {t && t !== 'running' && <span className={t.ok ? 'text-ok' : 'text-error'}>{t.message}</span>}
                  </div>
                </div>
              )
            })}
          </div>

          <h3 className="sub-title">Kategoriye göre sağlayıcı</h3>
          <p className="muted small">
            Örneğin altın Sağlamoğlu'ndan, döviz Harem'den gelsin. "Genel" seçilen kategori yukarıdaki etkin sağlayıcıyı kullanır.
            Tek bir ürünü farklı sağlayıcıya bağlamak için Fiyat Güncelleme'deki ürün kartından seçin.
          </p>
          <div className="category-grid">
            {Object.entries(categoryLabels).map(([key, label]) => (
              <label key={key} className="field">
                <span>{label}</span>
                <select
                  value={settings.categoryProviders?.[key] ?? ''}
                  onChange={(e) => {
                    const next = { ...(settings.categoryProviders ?? {}) }
                    if (e.target.value) next[key] = e.target.value
                    else delete next[key]
                    setSettings({ ...settings, categoryProviders: next })
                  }}
                >
                  <option value="">Genel ({providers.find((p) => p.key === settings.activeProvider)?.displayName})</option>
                  {providers.map((p) => <option key={p.key} value={p.key}>{p.displayName}</option>)}
                </select>
              </label>
            ))}
          </div>

          <div className="form-row">
            <label className="field">
              <span>Yedek sağlayıcı</span>
              <select value={settings.fallbackProvider ?? ''} onChange={(e) => setSettings({ ...settings, fallbackProvider: e.target.value || null })}>
                <option value="">Yok</option>
                {providers.filter((p) => p.key !== settings.activeProvider).map((p) => <option key={p.key} value={p.key}>{p.displayName}</option>)}
              </select>
            </label>
            <label className="field">
              <span>HTTP yoklama aralığı (sn)</span>
              <input type="number" min={2} max={300} value={settings.pollSeconds}
                onChange={(e) => setSettings({ ...settings, pollSeconds: Number(e.target.value) })} />
            </label>
            <label className="field">
              <span>Dükkan adı (sitede görünür)</span>
              <input value={settings.shopName} onChange={(e) => setSettings({ ...settings, shopName: e.target.value })} />
            </label>
          </div>

          {message && <div className={`alert ${message.ok ? 'ok' : 'error'}`}>{message.text}</div>}
          <button className="btn primary" onClick={save}>Kaydet</button>
        </div>
      </section>
      <CustomProviders onChanged={reloadProviders} />
    </div>
  )
}
