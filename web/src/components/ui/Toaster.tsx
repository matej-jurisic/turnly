import { useToastStore, type ToastTone } from '@/lib/toast'
import { cn } from '@/lib/utils'

const toneAccent: Record<ToastTone, string> = {
  success: 'border-l-success',
  error: 'border-l-destructive',
  info: 'border-l-primary',
}

const toneText: Record<ToastTone, string> = {
  success: 'text-success',
  error: 'text-destructive',
  info: 'text-primary',
}

const toneGlyph: Record<ToastTone, string> = {
  success: '✓',
  error: '!',
  info: 'i',
}

/** Global toast outlet. Mount once near the app root; driven by the `toast` helper. */
export function Toaster() {
  const toasts = useToastStore((s) => s.toasts)
  const dismiss = useToastStore((s) => s.dismiss)

  if (toasts.length === 0) return null

  return (
    <div className="pointer-events-none fixed inset-x-4 bottom-4 z-[60] flex flex-col gap-2 sm:left-auto sm:w-full sm:max-w-sm">
      {toasts.map((t) => (
        <div
          key={t.id}
          role="status"
          className={cn(
            'pointer-events-auto flex items-start gap-3 rounded-lg border border-l-4 border-border bg-card px-4 py-3 shadow-pop',
            toneAccent[t.tone],
          )}
        >
          <span
            className={cn(
              'mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-xs font-medium',
              toneText[t.tone],
            )}
            aria-hidden
          >
            {toneGlyph[t.tone]}
          </span>
          <p className="flex-1 text-sm text-foreground">{t.message}</p>
          <button
            onClick={() => dismiss(t.id)}
            className="shrink-0 text-muted-foreground hover:text-foreground"
            aria-label="Dismiss"
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  )
}
