import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const api = process.env.OZCAN_API ?? 'http://localhost:5095'

// Geliştirmede /api, /hubs ve /ws istekleri ASP.NET Core'a gider (CORS gerekmez).
// Üretim derlemesi Api'nin wwwroot klasörüne yazılır; tek süreç hem API'yi hem siteyi sunar.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5191,
    strictPort: true,
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/hubs': { target: api, changeOrigin: true, ws: true },
      '/ws': { target: api, changeOrigin: true, ws: true },
      '/health': { target: api, changeOrigin: true },
      '/media': { target: api, changeOrigin: true },
    },
  },
  build: {
    outDir: '../../src/OzcanDoviz.Api/wwwroot',
    emptyOutDir: true,
  },
})
