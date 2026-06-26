import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'net.turnly.app',
  appName: 'Turnly',
  // Vite builds the SPA here; `npx cap sync` copies it into the native project.
  webDir: 'dist',
  android: {
    // The WebView origin becomes https://localhost (vs the default http://), which keeps the
    // app on a secure-context origin and is what the backend allows via Cors:Origins.
    // The backend itself is the user-chosen remote server (see lib/server-config.ts).
  },
  server: {
    androidScheme: 'https',
  },
}

export default config
