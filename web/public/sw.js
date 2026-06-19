// Turnly service worker.
// Phase 8: receives Web Push messages and shows notifications.
// Plus a no-op fetch handler so the app is installable (real offline caching/app-shell is Phase 9).

// Pass-through fetch — no caching yet, but having a fetch handler satisfies
// installability heuristics in some browsers.
self.addEventListener('fetch', () => {})

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
