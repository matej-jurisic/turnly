import { create } from 'zustand'

export type ToastTone = 'success' | 'error' | 'info'

export interface Toast {
  id: number
  tone: ToastTone
  message: string
}

interface ToastState {
  toasts: Toast[]
  push: (tone: ToastTone, message: string) => void
  dismiss: (id: number) => void
}

const AUTO_DISMISS_MS = 4500

let nextId = 1

/** Backing store for the toaster. Prefer the imperative {@link toast} helper at call sites. */
export const useToastStore = create<ToastState>((set, get) => ({
  toasts: [],
  push: (tone, message) => {
    const id = nextId++
    set((s) => ({ toasts: [...s.toasts, { id, tone, message }] }))
    setTimeout(() => get().dismiss(id), AUTO_DISMISS_MS)
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}))

/**
 * Imperative toast API — usable outside React (event handlers, mutation callbacks) the same
 * way the old `alert()` calls were. Rendered by `<Toaster />`, mounted once at the app root.
 */
export const toast = {
  success: (message: string) => useToastStore.getState().push('success', message),
  error: (message: string) => useToastStore.getState().push('error', message),
  info: (message: string) => useToastStore.getState().push('info', message),
}
