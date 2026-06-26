import { Preferences } from '@capacitor/preferences'
import { isNative } from '@/lib/native'

/**
 * The native app has no same-origin server, so the user picks which self-hosted Turnly backend
 * to talk to (e.g. https://turnly.myhome.net) on first launch. We persist that origin in native
 * storage and resolve the API base from it (see `lib/api.ts`).
 *
 * On the web this module is inert: `getServerOrigin()` returns null and the API base stays `/api`.
 */
const KEY = 'turnly.serverOrigin'

// Synchronous cache so request building never has to await storage. Hydrated once at startup by
// `loadServerOrigin()` and kept in sync by `setServerOrigin()`/`clearServerOrigin()`.
let cached: string | null = null

/** The saved server origin (already normalized), or null if none is configured. */
export function getServerOrigin(): string | null {
  return cached
}

/** Hydrate the cache from native storage. No-op on the web. Call once at app startup. */
export async function loadServerOrigin(): Promise<string | null> {
  if (!isNative()) return null
  const { value } = await Preferences.get({ key: KEY })
  cached = value ?? null
  return cached
}

/** Persist (and cache) the chosen server origin after normalization. */
export async function setServerOrigin(input: string): Promise<string> {
  const normalized = normalizeOrigin(input)
  await Preferences.set({ key: KEY, value: normalized })
  cached = normalized
  return normalized
}

/** Forget the configured server (used by "Change server"). */
export async function clearServerOrigin(): Promise<void> {
  await Preferences.remove({ key: KEY })
  cached = null
}

/**
 * Normalize user input into a bare origin: trim, strip trailing slashes and any `/api` suffix,
 * and default the scheme to https when omitted. Throws on input that can't be parsed as a URL.
 */
export function normalizeOrigin(input: string): string {
  let s = input.trim().replace(/\/+$/, '')
  if (!s) throw new Error('Server address is required.')
  if (!/^https?:\/\//i.test(s)) s = `https://${s}`
  s = s.replace(/\/api$/i, '')
  // Validate; `URL` throws on malformed input.
  const url = new URL(s)
  return `${url.protocol}//${url.host}`
}
