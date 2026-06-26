import { PushNotifications } from '@capacitor/push-notifications'
import { notificationsApi } from '@/lib/api'
import { isNative } from '@/lib/native'

/**
 * Native (FCM) push for the Android app. The WebView can't do Web Push, so we register an FCM token
 * with the backend and handle taps here (the native equivalent of `web/public/sw.js`). No-op on web.
 */

let lastToken: string | null = null
let listenersAttached = false
// The latest tap handler, refreshed on each register call so deep links use the current navigator.
let onOpenChore: (url: string) => void = () => {}

/** The most recent FCM token registered for this device, or null. */
export function getNativePushToken(): string | null {
  return lastToken
}

/** The native push permission state, or 'unsupported' off-native. */
export async function nativePushPermission(): Promise<
  'granted' | 'denied' | 'prompt' | 'unsupported'
> {
  if (!isNative()) return 'unsupported'
  const permission = await PushNotifications.checkPermissions()
  if (permission.receive === 'granted') return 'granted'
  if (permission.receive === 'denied') return 'denied'
  return 'prompt'
}

/**
 * Requests permission, registers for FCM, and reports the token to the backend. `onOpen` is
 * invoked when the user taps a notification, with the deep-link url (mirrors the web tap handler).
 * Safe to call repeatedly (listeners attach once) and on web (returns without doing anything).
 * Returns true once a token registration was kicked off (permission granted).
 */
export async function registerNativePush(onOpen: (url: string) => void): Promise<boolean> {
  if (!isNative()) return false
  onOpenChore = onOpen

  // Attach the FCM listeners exactly once; they persist for the app's lifetime.
  if (!listenersAttached) {
    await PushNotifications.addListener('registration', async (token) => {
      lastToken = token.value
      try {
        await notificationsApi.fcmSubscribe(token.value)
      } catch {
        // Best-effort: a failed register just means no native push until the next attempt.
      }
    })
    await PushNotifications.addListener('pushNotificationActionPerformed', (action) => {
      const data = (action.notification.data ?? {}) as Record<string, unknown>
      const url = typeof data.url === 'string' ? data.url : '/chores'
      onOpenChore(url)
    })
    listenersAttached = true
  }

  let permission = await PushNotifications.checkPermissions()
  if (permission.receive === 'prompt' || permission.receive === 'prompt-with-rationale') {
    permission = await PushNotifications.requestPermissions()
  }
  if (permission.receive !== 'granted') return false

  await PushNotifications.register()
  return true
}

/** Drops this device's FCM token server-side (best-effort), e.g. on sign-out. */
export async function unregisterNativePush(): Promise<void> {
  if (!isNative() || !lastToken) return
  try {
    await notificationsApi.fcmUnsubscribe(lastToken)
  } catch {
    // Best-effort.
  }
  lastToken = null
}
