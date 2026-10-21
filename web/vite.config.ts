import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

// Identifies this build. Baked into the bundle and written to version.json, which the server reports
// back so a tab still running an older bundle can offer to reload (see lib/app-version.ts).
const buildId = new Date().toISOString()

// https://vite.dev/config/
export default defineConfig({
  define: {
    __BUILD_ID__: JSON.stringify(buildId),
  },
  plugins: [
    react(),
    tailwindcss(),
    {
      name: 'turnly-build-id',
      apply: 'build',
      generateBundle() {
        this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ build: buildId }) })
      },
    },
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    // Proxy the API to the backend so the frontend is same-origin in dev
    // (the httpOnly refresh cookie then "just works").
    proxy: {
      '/api': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
    },
  },
})
