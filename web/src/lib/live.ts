import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { apiBase, tryRefresh } from '@/lib/api'
import { isNative } from '@/lib/native'
import { useAuthStore } from '@/store/auth'

// The server sends a heartbeat every 20s; silence for this long means the connection is dead.
const SILENCE_TIMEOUT_MS = 45_000
const MAX_RETRY_DELAY_MS = 30_000
// Collapses a burst of change events (and our own writes echoing back) into one refetch.
const REFETCH_DEBOUNCE_MS = 250

/** Opens the `/api/events` stream. fetch rather than EventSource, which can't send the bearer token. */
async function openStream(signal: AbortSignal): Promise<Response> {
  const open = () => {
    const headers = new Headers({ Accept: 'text/event-stream' })
    const token = useAuthStore.getState().accessToken
    if (token) headers.set('Authorization', `Bearer ${token}`)
    if (isNative()) headers.set('X-Turnly-Client', 'android')
    return fetch(`${apiBase()}/events`, { headers, signal, credentials: 'include' })
  }
  const res = await open()
  if (res.status === 401 && (await tryRefresh())) return open()
  return res
}

/**
 * Keeps a server event stream open and calls `onChange` whenever someone changed shared data.
 * The stream is dropped while the page is hidden (mobile browsers kill it anyway) and reopened
 * on return; every reopen after a gap also fires `onChange` to catch up on what was missed.
 * Returns a stop function.
 */
export function startLiveUpdates(onChange: () => void): () => void {
  let stopped = false
  let controller: AbortController | null = null
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let debounceTimer: ReturnType<typeof setTimeout> | undefined
  let failures = 0
  let missedEvents = false

  const notify = () => {
    clearTimeout(debounceTimer)
    debounceTimer = setTimeout(onChange, REFETCH_DEBOUNCE_MS)
  }

  async function connect() {
    clearTimeout(retryTimer)
    if (stopped || document.hidden || controller) return
    const own = (controller = new AbortController())
    let watchdog: ReturnType<typeof setTimeout> | undefined
    const armWatchdog = () => {
      clearTimeout(watchdog)
      watchdog = setTimeout(() => own.abort(), SILENCE_TIMEOUT_MS)
    }

    try {
      armWatchdog()
      const res = await openStream(own.signal)
      if (!res.ok || !res.body) throw new Error('stream unavailable')
      failures = 0
      if (missedEvents) notify()
      missedEvents = false

      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        armWatchdog()
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        if (lines.some((line) => line.startsWith('event: change'))) notify()
      }
    } catch {
      // Aborted, offline or server restarting: handled by the retry below.
    } finally {
      clearTimeout(watchdog)
    }

    missedEvents = true
    // A newer connection (or a hide) has already taken over.
    if (controller !== own) return
    controller = null
    if (stopped || document.hidden) return
    failures += 1
    retryTimer = setTimeout(() => void connect(), Math.min(MAX_RETRY_DELAY_MS, 1000 * 2 ** (failures - 1)))
  }

  const onVisibility = () => {
    if (document.hidden) {
      clearTimeout(retryTimer)
      controller?.abort()
      controller = null
      missedEvents = true
    } else {
      failures = 0
      void connect()
    }
  }
  // Coming back online shouldn't wait out the backoff.
  const onOnline = () => {
    failures = 0
    void connect()
  }

  document.addEventListener('visibilitychange', onVisibility)
  window.addEventListener('online', onOnline)
  void connect()

  return () => {
    stopped = true
    document.removeEventListener('visibilitychange', onVisibility)
    window.removeEventListener('online', onOnline)
    clearTimeout(retryTimer)
    clearTimeout(debounceTimer)
    controller?.abort()
    controller = null
  }
}

/** Refetches whatever is on screen when another client (or the server's own scheduler) changes data. */
export function useLiveUpdates() {
  const queryClient = useQueryClient()
  useEffect(() => startLiveUpdates(() => void queryClient.invalidateQueries()), [queryClient])
}
