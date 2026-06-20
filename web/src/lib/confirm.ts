import { create } from 'zustand'

export interface ConfirmOptions {
  title?: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  /** Styles the confirm button — destructive actions use `danger`. Defaults to `danger`. */
  variant?: 'danger' | 'primary'
}

interface PendingConfirm extends ConfirmOptions {
  resolve: (ok: boolean) => void
}

interface ConfirmState {
  pending: PendingConfirm | null
  request: (opts: ConfirmOptions) => Promise<boolean>
  settle: (ok: boolean) => void
}

/** Backing store for the confirm dialog. Prefer the {@link confirm} helper at call sites. */
export const useConfirmStore = create<ConfirmState>((set, get) => ({
  pending: null,
  request: (opts) =>
    new Promise<boolean>((resolve) => {
      set({ pending: { ...opts, resolve } })
    }),
  settle: (ok) => {
    const { pending } = get()
    pending?.resolve(ok)
    set({ pending: null })
  },
}))

/**
 * Promise-based confirmation, a styled drop-in for `window.confirm`. Resolves `true` when the
 * user confirms, `false` otherwise. Rendered by `<ConfirmHost />`, mounted once at the app root.
 */
export function confirm(opts: ConfirmOptions): Promise<boolean> {
  return useConfirmStore.getState().request(opts)
}
