// Turnly push-only service worker (Phase 8).
// Receives Web Push messages and shows notifications. The full PWA (offline, app shell,
// caching, install) is Phase 9 — this file deliberately handles only push + clicks.

self.addEventListener('push', (event) => {
  let data = {}
  try {
    data = event.data ? event.data.json() : {}
  } catch {
    data = { title: 'Turnly', body: event.data ? event.data.text() : '' }
  }

  const title = data.title || 'Turnly'
  const options = {
    body: data.body || '',
    data: { url: data.url || '/chores' },
  }

  event.waitUntil(self.registration.showNotification(title, options))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const targetUrl = (event.notification.data && event.notification.data.url) || '/chores'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if ('focus' in client) {
          client.navigate(targetUrl)
          return client.focus()
        }
      }
      return self.clients.openWindow(targetUrl)
    }),
  )
})
