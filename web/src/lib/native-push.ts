import { PushNotifications } from '@capacitor/push-notifications'
import { notificationsApi } from '@/lib/api'
import { isNative } from '@/lib/native'

/**
 * Native (FCM) push for the Android app. The WebView can't do Web Push, so we register an FCM token
 * with the backend and handle taps here (the native equivalent of `web/public/sw.js`). No-op on web.
 */

let lastToken: string | null = null

/** The most recent FCM token registered for this device, or null. */
export function getNativePushToken(): string | null {
  return lastToken
}

/**
 * Requests permission, registers for FCM, and reports the token to the backend. `onOpenChore` is
 * invoked when the user taps a notification, with the deep-link url (mirrors the web tap handler).
 * Returns a cleanup that removes the listeners. Safe to call on web (returns a no-op).
 */
export async function registerNativePush(
  onOpenChore: (url: string) => void,
): Promise<() => void> {
  if (!isNative()) return () => {}

  let permission = await PushNotifications.checkPermissions()
  if (permission.receive === 'prompt' || permission.receive === 'prompt-with-rationale') {
    permission = await PushNotifications.requestPermissions()
  }
  if (permission.receive !== 'granted') return () => {}

  const handles = [
    await PushNotifications.addListener('registration', async (token) => {
      lastToken = token.value
      try {
        await notificationsApi.fcmSubscribe(token.value)
      } catch {
        // Best-effort: a failed register just means no native push until the next attempt.
      }
    }),
    await PushNotifications.addListener('pushNotificationActionPerformed', (action) => {
      const data = (action.notification.data ?? {}) as Record<string, unknown>
      const url = typeof data.url === 'string' ? data.url : '/chores'
      onOpenChore(url)
    }),
  ]

  await PushNotifications.register()

  return () => {
    for (const h of handles) void h.remove()
  }
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
