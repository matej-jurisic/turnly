// Centralized browser-history integration for modals: the device/browser Back button closes the
// topmost open modal instead of navigating away, and opening/closing modals never leaks history
// entries — including React StrictMode's dev double-mount and rapid open/close ("spamming").
//
// Model: each open modal owns one throwaway history entry ("sentinel"). A Back press pops the top
// sentinel (browser-driven) and we close the matching modal. A close via the UI/programmatically
// instead schedules a `history.back()` to drop the sentinel — but deferred to a later task, so an
// immediate re-open (a StrictMode remount, or fast toggling) can cancel it and reuse the existing
// sentinel rather than stacking a duplicate. That deferral is what stops entries from accumulating.

type Closer = () => void

const openers: Closer[] = []
let scheduledClose: ReturnType<typeof setTimeout> | undefined
let listening = false

function hasSentinel(): boolean {
  return Boolean((window.history.state as { turnlyModal?: boolean } | null)?.turnlyModal)
}

function handlePopState(): void {
  // Back/forward fired. A user Back supersedes any close we had deferred.
  if (scheduledClose !== undefined) {
    clearTimeout(scheduledClose)
    scheduledClose = undefined
  }
  // The browser already removed the top sentinel; close the matching (topmost) modal.
  openers.pop()?.()
}

function ensureListening(): void {
  if (listening) return
  listening = true
  window.addEventListener('popstate', handlePopState)
}

/** Call when a modal opens; returns a cleanup to run when it closes (for any reason). */
export function registerModal(onClose: Closer): () => void {
  ensureListening()
  if (scheduledClose !== undefined) {
    // A close was pending (StrictMode remount or rapid reopen): cancel it and reuse the sentinel
    // that was about to be dropped, instead of pushing a duplicate entry.
    clearTimeout(scheduledClose)
    scheduledClose = undefined
  } else {
    window.history.pushState({ turnlyModal: true }, '')
  }
  openers.push(onClose)

  return () => {
    const idx = openers.lastIndexOf(onClose)
    if (idx === -1) return // already removed via Back (handlePopState)
    openers.splice(idx, 1)
    // Skip the pop if a real route navigation happened while open — the top state is then the
    // router's, not our sentinel, and we must not undo that navigation.
    if (!hasSentinel()) return
    scheduledClose = setTimeout(() => {
      scheduledClose = undefined
      if (hasSentinel()) window.history.back()
    }, 0)
  }
}
