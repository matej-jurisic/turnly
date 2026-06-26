import { Preferences } from '@capacitor/preferences'
import { isNative } from '@/lib/native'

/**
 * The native app can't keep its refresh token in a cross-origin httpOnly cookie, so the backend
 * hands it back in the auth response body and we persist it in device storage. On the web this
 * module is inert (the cookie does the job) and `getRefreshToken()` stays null.
 */
const KEY = 'turnly.refreshToken'

let cached: string | null = null

/** The stored refresh token (native only), or null. */
export function getRefreshToken(): string | null {
  return cached
}

/** Hydrate the cache from device storage. No-op on the web. Call once at app startup. */
export async function loadRefreshToken(): Promise<void> {
  if (!isNative()) return
  const { value } = await Preferences.get({ key: KEY })
  cached = value ?? null
}

/** Persist (or clear, when passed null) the refresh token. No-op on the web. */
export async function setRefreshToken(token: string | null): Promise<void> {
  cached = token
  if (!isNative()) return
  if (token) await Preferences.set({ key: KEY, value: token })
  else await Preferences.remove({ key: KEY })
}
