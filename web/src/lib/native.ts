import { Capacitor } from '@capacitor/core'

/**
 * True when running inside the Capacitor native shell (the Android APK), false in any
 * browser/PWA context. This is the single switch the app branches on so the web path stays
 * exactly as it was: same-origin `/api`, cookie-based refresh, Web Push.
 */
export function isNative(): boolean {
  return Capacitor.isNativePlatform()
}
