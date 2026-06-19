import { notificationsApi } from '@/lib/api'

/** Whether this browser can do Web Push at all. */
export function pushSupported(): boolean {
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
}

/** Current notification permission state, or 'unsupported'. */
export function pushPermission(): NotificationPermission | 'unsupported' {
  if (!pushSupported()) return 'unsupported'
  return Notification.permission
}

function urlBase64ToUint8Array(base64String: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = window.atob(base64)
  const output = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i)
  return output
}

function subscriptionToRequest(sub: PushSubscription) {
  const json = sub.toJSON()
  const keys = json.keys ?? {}
  return { endpoint: sub.endpoint, p256dh: keys.p256dh ?? '', auth: keys.auth ?? '' }
}

/** Requests permission, subscribes via the push manager, and registers the subscription server-side. */
export async function enablePush(): Promise<void> {
  if (!pushSupported()) throw new Error('Push notifications are not supported in this browser.')

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') throw new Error('Notification permission was not granted.')

  const registration = await navigator.serviceWorker.ready
  const { publicKey } = await notificationsApi.vapidKey()
  if (!publicKey) throw new Error('The server is not configured for push notifications (no VAPID key).')

  const existing = await registration.pushManager.getSubscription()
  const subscription =
    existing ??
    (await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    }))

  await notificationsApi.subscribe(subscriptionToRequest(subscription))
}

/** Unsubscribes this device locally and server-side. */
export async function disablePush(): Promise<void> {
  if (!pushSupported()) return
  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) return
  const endpoint = subscription.endpoint
  await subscription.unsubscribe()
  await notificationsApi.unsubscribe(endpoint)
}

/** Whether this device currently has an active push subscription. */
export async function isPushEnabled(): Promise<boolean> {
  if (!pushSupported() || Notification.permission !== 'granted') return false
  const registration = await navigator.serviceWorker.ready
  return (await registration.pushManager.getSubscription()) != null
}

/** This browser's current push endpoint, used to mark "this device" in the device list. */
export async function getCurrentEndpoint(): Promise<string | null> {
  if (!pushSupported() || Notification.permission !== 'granted') return null
  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.getSubscription()
  return subscription?.endpoint ?? null
}
