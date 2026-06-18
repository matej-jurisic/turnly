import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
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
